using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace HDTplugins.Services
{
    public static class Log
    {
        public static void Info(string msg) => Debug.WriteLine(msg);
        public static void Warn(string msg) => Debug.WriteLine("WARN " + msg);
    }
}