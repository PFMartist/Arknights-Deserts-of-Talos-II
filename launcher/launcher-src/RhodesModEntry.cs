using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

// Launcher entry by PFMartist / GPT(Codex).
[assembly: AssemblyTitle("罗德岛 MOD 启动器")]
[assembly: AssemblyCopyright("PFMartist / GPT(Codex)")]

internal static class RhodesModEntry
{
    private const string ConsoleTitle = "罗德岛 MOD 启动器";
    private const int DefaultIconResourceId = 32512;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(
        IntPtr instance, IntPtr name, uint type, int width, int height, uint flags);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleIcon(IntPtr icon);

    [STAThread]
    private static int Main()
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            SetConsoleAppearance();

            string entryPath = Assembly.GetExecutingAssembly().Location;
            string packageDirectory = Path.GetDirectoryName(entryPath);
            string scriptPath = Path.Combine(packageDirectory, "RhodesMod.ps1");
            if (!File.Exists(scriptPath))
            {
                Console.Error.WriteLine("找不到启动脚本，请将启动器与完整安装包一起解压。");
                Console.Error.WriteLine(scriptPath);
                return Finish(2);
            }

            string powerShellPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(powerShellPath))
            {
                Console.Error.WriteLine("找不到 Windows PowerShell 5.1，无法启动控制台菜单。");
                Console.Error.WriteLine(powerShellPath);
                return Finish(3);
            }

            Console.WriteLine("正在启动罗德岛 MOD 控制台菜单……");

            // Windows file paths cannot contain a double quote. The fixed script
            // filename also prevents a trailing backslash before the closing quote.
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = powerShellPath;
            startInfo.Arguments = "-NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File \""
                + scriptPath + "\" -Action Menu";
            startInfo.WorkingDirectory = packageDirectory;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = false;
            startInfo.ErrorDialog = false;
            // Standard input, output and error stay attached to this console.

            using (Process child = new Process())
            {
                child.StartInfo = startInfo;
                if (!child.Start())
                {
                    throw new InvalidOperationException("无法创建 Windows PowerShell 进程。");
                }
                child.WaitForExit();
                return Finish(child.ExitCode);
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("启动罗德岛 MOD 控制台菜单失败。");
            Console.Error.WriteLine(error.Message);
            return Finish(1);
        }
    }

    private static void SetConsoleAppearance()
    {
        try
        {
            Console.Title = ConsoleTitle;
            // csc /win32icon emits group icon resource 32512. LR_SHARED keeps
            // this resource handle alive for the lifetime of the executable.
            IntPtr icon = LoadImage(GetModuleHandle(null),
                new IntPtr(DefaultIconResourceId), 1, 0, 0, 0x8040);
            if (icon != IntPtr.Zero)
            {
                SetConsoleIcon(icon);
            }
        }
        catch
        {
            // Console hosts control whether a window icon can be displayed.
        }
    }

    private static int Finish(int exitCode)
    {
        try
        {
            if (exitCode != 0 && !Console.IsInputRedirected && !Console.IsOutputRedirected)
            {
                Console.Error.WriteLine("按回车关闭启动器……");
                Console.ReadLine();
            }
        }
        catch
        {
            // A console may already have closed. Keep the original exit code.
        }
        return exitCode;
    }
}
