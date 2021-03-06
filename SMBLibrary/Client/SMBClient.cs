﻿using SMBLibrary.Server.SMB2;
using System;
using System.Collections.Generic;
using System.Text;

namespace SMBLibrary.Client
{
    public class SMBClient : IDisposable
    {
        public Enums.SMBClientVersion Version { get; private set; }
        protected ISMBClient Client { get; private set; }        

        public SMBClient(Enums.SMBClientVersion version, int connectionTimeout = 10000)
        {
            Version = version;
            switch (Version)
            {
                case Enums.SMBClientVersion.Version1:
                    Client = new SMB1Client { ConnectionTimeout = connectionTimeout };                    
                    break;
                case Enums.SMBClientVersion.Version2:
                    Client = new SMB2Client { ConnectionTimeout = connectionTimeout };
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        public Enums.SMBClientStatus Login(System.Net.IPAddress address, string username, string password, string domain = "")
        {
            if (!Client.Connect(address, SMBTransportType.DirectTCPTransport))
            {
                return Enums.SMBClientStatus.CantConnect;
            }
            if (Client.Login(domain, username, password) != NTStatus.STATUS_SUCCESS)
            {
                return Enums.SMBClientStatus.LoginFailed;
            }
            return Enums.SMBClientStatus.LoggedIn;
        }

        public void Logoff()
        {
            Client.Logoff();
        }

        ISMBFileStore _fileStore;

        public void OpenShare(string share)
        {
            _fileStore = Client.TreeConnect(share, out var status);
            if (status != NTStatus.STATUS_SUCCESS)
                throw new SMBClientException("Open share failed", status);
        }

        public void CloseShare()
        {
            _fileStore?.Disconnect();
            _fileStore = null;
        }

        public SMBClientFileStream GetSMBFileStream(string path, AccessMask accessMask, ShareAccess shareAccess = ShareAccess.Write | ShareAccess.Read, CreateDisposition createDisposition = CreateDisposition.FILE_OPEN)
        {
            CheckFileStore();
            _fileStore.CreateFile(out object handle, out var fileStatus, path, accessMask, 0, shareAccess, createDisposition, CreateOptions.FILE_NON_DIRECTORY_FILE, null);
            if (fileStatus != FileStatus.FILE_OPENED && fileStatus != FileStatus.FILE_CREATED && fileStatus != FileStatus.FILE_OVERWRITTEN)
                throw new SMBClientException("Error in GetSMBFileStream", fileStatus);
            return new SMBClientFileStream(_fileStore, handle);
        }

        private void CheckFileStore()
        {
            if (_fileStore == null)
                throw new SMBClientException("call OpenShare first");
        }

        public bool FileExists(string path)
        {
            return Exists(path, false);
        }

        public void DeleteFile(string path)
        {
            CheckFileStore();
            Server.SMB1.SMB1FileStoreHelper.DeleteFile(_fileStore, path, null);
        }

        public void DeleteDirectory(string path)
        {
            CheckFileStore();
            Server.SMB1.SMB1FileStoreHelper.DeleteDirectory(_fileStore, path, null);
        }

        public bool DirectoryExists(string path)
        {
            return Exists(path, true);
        }

        bool Exists(string path, bool isDirectory)
        {
            CheckFileStore();
            _fileStore.CreateFile(out object handle, out var fileStatus, path, AccessMask.MAXIMUM_ALLOWED, 0, ShareAccess.Read, CreateDisposition.FILE_OPEN, isDirectory ? CreateOptions.FILE_DIRECTORY_FILE : CreateOptions.FILE_NON_DIRECTORY_FILE, null);

            if (fileStatus != FileStatus.FILE_OPENED)
                return false;
            _fileStore.CloseFile(handle);
            return true;
        }

        public void Rename(string fromPath, string toPath)
        {
            CheckFileStore();
            _fileStore.CreateFile(out object handle, out var fileStatus, fromPath, AccessMask.MAXIMUM_ALLOWED, 0, ShareAccess.Read, CreateDisposition.FILE_OPEN, CreateOptions.FILE_NON_DIRECTORY_FILE, null);
            if (fileStatus != FileStatus.FILE_OPENED)
                throw new SMBClientException("Error in Rename", fileStatus);
            try
            {
                var status = _fileStore.SetFileInformation(handle, new FileRenameInformationType2 { FileName = toPath, ReplaceIfExists = true });
                if (status != NTStatus.STATUS_SUCCESS)
                    throw new SMBClientException("Error on rename", status);
            }
            finally
            {
                _fileStore.CloseFile(handle);
            }
        }

        public List<ListEntry> ListContent(string path)
        {
            CheckFileStore();
            _fileStore.CreateFile(out object handle, out var fileStatus, path, AccessMask.GENERIC_READ, 0, ShareAccess.Read, CreateDisposition.FILE_OPEN, CreateOptions.FILE_DIRECTORY_FILE, null);
            if (fileStatus != FileStatus.FILE_OPENED)
                throw new SMBClientException("Error in ListContent", fileStatus);
            try
            {
                _fileStore.QueryDirectory(out var items, handle, "*", FileInformationClass.FileDirectoryInformation);
                var lst = new List<ListEntry>();
                foreach (FileDirectoryInformation i in items)
                {
                    if (i.FileName == "." || i.FileName == "..")
                        continue;
                    var li = new ListEntry();
                    li.Name = i.FileName;
                    li.Size = i.AllocationSize;
                    li.Attributes = i.FileAttributes;
                    lst.Add(li);
                }
                return lst;
            }
            finally
            {
                _fileStore.CloseFile(handle);
            }
        }

        public List<string> Shares
        {
            get
            {
                var lst = Client.ListShares(out var status);
                if (status == NTStatus.STATUS_PENDING)
                {
                    lst = Client.ListShares(out status);
                }
                return lst;
            }
        }

        public void Dispose()
        {
            Client?.Disconnect();
            Client = null;
        }
    }
}
