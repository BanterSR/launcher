namespace Momoko.Launcher.Core;

// Files compiled into the executable as embedded resources (MomokoLauncher.csproj):
// ui/** , games/<game>/version.dll and THIRD_PARTY.md (served as ui/THIRD_PARTY.md).
// Logical names use forward slashes, e.g. "ui/index.html".
internal static class Embedded
{
    public static byte[]? Read(string name)
    {
        using var stream=typeof(Embedded).Assembly.GetManifestResourceStream(name);
        if(stream is null)return null;
        var data=new byte[stream.Length];
        stream.ReadExactly(data);
        return data;
    }

    public static byte[] Require(string name)=>Read(name)??throw new FileNotFoundException("启动器缺少内置文件："+name,name);
}
