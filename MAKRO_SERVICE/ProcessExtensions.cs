using System;
using System.Diagnostics;
using System.Linq;
namespace MAKRO_SERVICE;
public static class ProcessExtensions
{
    public static bool IsSystemProcess(this Process process)
    {
        try
        {
            return process.SessionId == 0;
        }
        catch (Exception)
        {
            // Falls wir keine Berechtigung haben, auf die SessionId zuzugreifen
            return false;
        }
    }
}


