namespace AutoUploadToFTP
{
    internal class Dir
    {
        public Dir(string name)
        {
            Name = name;
            Files = new List<FileInfo>();
            SubDirs = new List<Dir>();
        }

        public string Name { get; private set; }
        public IList<FileInfo> Files { get; private set; }

        public IList<Dir> SubDirs { get; private set; }

        public void AddFile(FileInfo file)
        {
            Files.Add(file);
        }

        public void AddDir(Dir dir)
        {
            SubDirs.Add(dir);
        }
    }

    internal class UploadFileInfo
    {
        public string Path { get; set; }

        public string FileName { get; set; }
        /// <summary>
        /// 0-new file 1-update file 2-update failed file
        /// </summary>
        public int FileType { get; set; }


        public string FileTypeText => FileType == 0 ? "new file" : (FileType == 1 ? "update file" : "update failed file");
    }



}
