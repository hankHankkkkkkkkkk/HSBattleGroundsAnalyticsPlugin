using System;
using System.Collections.Generic;
using System.Text;

namespace HDTplugins.Services
{
    public static class ArchiveKeyProvider
    {
        public static string GetCurrentArchiveKey()
        {
            // TODO: 后面会按 season-patch + 自定义归档名实现
            return "season12-34.6.0";
        }
    }
}
