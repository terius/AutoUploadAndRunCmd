# AutoUploadAndRunCmd

You can monitor the changes of the files in the local folder and automatically upload the new or updated files to the remote server (via ftp login), and after the successful upload, you can automatically login to the remote server to execute the configured commands.

Usage：  
1. Edit the configuration items in the config file  
**Host**: the address of the remote server  
**Username**: the user name for logging in to the remote server, if not filled in, the default is root.  
**Password**: the password for logging in to the remote server.  
**Sshport**: the SSH port for logging in to the remote server, if not filled in, the default is 22.  
**Ftpport**: the FTP port for logging in to the remote server, if not filled in, the default is 22.  
**Localpath**: local windows directory for monitoring.  
**Remotepath**: the directory where you want to upload files to the remote server, you must create this directory before running the software.  
**Cmd**: the command to be executed on the remote server after uploading files.  
**UploadAllFilesWhenAppStart**: whether to upload all files in the monitored folder to the remote server when the software starts, if not, the default is 0 (false).  
**CheckTime**: When the program starts, it checks the files whose change time is greater than this time according to this time, if it is empty, the checking time is set to the current time, and this item will be set to empty when the program polls all the files in the monitored folder for the first time. If UploadAllFilesWhenAppStart is set to 1, this setting is ignored.  
3. Run AutoUploadToFTP.exe  
Have fun :)
