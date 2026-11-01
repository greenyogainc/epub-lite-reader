using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace EpubLiteReader.Tests;

/// <summary>Adds a deny entry for the current user on a file or directory and removes it
/// again on dispose, so a test can force UnauthorizedAccessException and its temp folder
/// can still be cleaned up. Always use it in a <c>using</c> or a finally block.</summary>
internal sealed class DenyAccess : IDisposable
{
    private readonly string _path;
    private readonly bool _isDirectory;
    private readonly FileSystemAccessRule _rule;

    private DenyAccess(string path, bool isDirectory, FileSystemRights rights)
    {
        _path = path;
        _isDirectory = isDirectory;
        _rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, rights, AccessControlType.Deny);

        if (isDirectory)
        {
            var info = new DirectoryInfo(path);
            var security = info.GetAccessControl();
            security.AddAccessRule(_rule);
            info.SetAccessControl(security);
        }
        else
        {
            var info = new FileInfo(path);
            var security = info.GetAccessControl();
            security.AddAccessRule(_rule);
            info.SetAccessControl(security);
        }
    }

    /// <summary>Opening the file for reading now fails with UnauthorizedAccessException.</summary>
    public static DenyAccess ReadingFile(string path) => new(path, isDirectory: false, FileSystemRights.ReadData);

    /// <summary>Listing the directory now fails with UnauthorizedAccessException.</summary>
    public static DenyAccess ListingDirectory(string path) => new(path, isDirectory: true, FileSystemRights.ListDirectory);

    public void Dispose()
    {
        if (_isDirectory)
        {
            var info = new DirectoryInfo(_path);
            var security = info.GetAccessControl();
            security.RemoveAccessRule(_rule);
            info.SetAccessControl(security);
        }
        else
        {
            var info = new FileInfo(_path);
            var security = info.GetAccessControl();
            security.RemoveAccessRule(_rule);
            info.SetAccessControl(security);
        }
    }
}
