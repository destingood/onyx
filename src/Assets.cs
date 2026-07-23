using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;

namespace BTOptimizer
{
    /// <summary>Images embarquées (mascotte docteur, badges) style DTG.</summary>
    internal static class Assets
    {
        private static readonly Dictionary<string, Image> _cache = new Dictionary<string, Image>();

        public static Image Get(string logicalName)
        {
            if (_cache.ContainsKey(logicalName)) return _cache[logicalName];
            Image img = null;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var s = asm.GetManifestResourceStream(logicalName))
                    if (s != null) img = Image.FromStream(s);
            }
            catch { }
            _cache[logicalName] = img;
            return img;
        }

        public static Image DoctorFinger { get { return Get("doctor-finger.png"); } }
        public static Image BadgePremierSoin { get { return Get("badge-premiersoin.png"); } }
    }
}
