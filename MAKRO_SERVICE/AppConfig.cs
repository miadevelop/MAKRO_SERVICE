using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MAKRO_SERVICE;

public class AppConfig
{
    public string LogDirectory { get; set; }
    public string LogFileName { get; set; }
    public string KeyMappingFile { get; set; }
    public bool DebugMode { get; set; }
}
