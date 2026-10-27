using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace LockSuite.Service.Security;

/// <summary>
/// Provee utilidades para endurecer los permisos NTFS del directorio %ProgramData%\LockSuite.
/// Elimina la herencia de C:\ProgramData y restringe el acceso exclusivamente a SYSTEM y Administradores.
/// </summary>
public static class AclHelper
{
    public static void SecureProgramDataDirectory()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        try
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string locksuiteDir = Path.Combine(programData, "LockSuite");
            if (!Directory.Exists(locksuiteDir))
            {
                Directory.CreateDirectory(locksuiteDir);
            }

            string icaclsPath = Path.Combine(Environment.SystemDirectory, "icacls.exe");
            if (!File.Exists(icaclsPath)) return;

            var startInfo = new ProcessStartInfo
            {
                FileName = icaclsPath,
                Arguments = $"\"{locksuiteDir}\" /inheritance:r /grant:r \"*S-1-5-18:(OI)(CI)F\" \"*S-1-5-32-544:(OI)(CI)F\" /T",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var proc = Process.Start(startInfo);
            proc?.WaitForExit(5000);
        }
        catch
        {
            // Best-effort: no interrumpe el arranque del servicio si icacls falla
        }
    }
}
