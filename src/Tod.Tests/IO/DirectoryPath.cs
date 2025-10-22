namespace Tod.Tests.IO;

internal sealed class DirectoryPath(string path)
{
    public static DirectoryPath Temp => new(System.IO.Path.GetTempPath());

    public string Path { get; } = System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;

    public bool Exists => Directory.Exists(Path);

    private void Create()
    {
        Directory.CreateDirectory(Path);
    }

    private void CreateNew()
    {
        DirectoryPath temp;
        do
        {
            temp = new DirectoryPath(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar) + "." + Guid.NewGuid());
        }
        while (temp.Exists);
        try
        {
            temp.Create();
            Directory.Move(temp.Path, Path);
        }
        finally
        {
            if (temp.Exists)
            {
                temp.DeleteIgnoringErrors();
            }
        }
    }

    public DirectoryPath CreateUnique(string name)
    {
        var index = 0;
        while (true)
        {
            var temp = new DirectoryPath(System.IO.Path.Combine(Path, name + (index == 0 ? "" : "-" + index)));
            try
            {
                temp.CreateNew();
                return temp;
            }
            catch (Exception e)
            {
                if (index >= 1000)
                {
                    throw new IOException($"Failed to create {System.IO.Path.Combine(Path, name)}", e);
                }
                index++;
            }
        }
    }

    public void DeleteIgnoringErrors()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
        }
    }
}
