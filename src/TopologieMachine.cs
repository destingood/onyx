using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace BTOptimizer
{
    /// <summary>
    /// La moitié MACHINE de <see cref="Topologie"/> : lire le processeur (cœurs, classes
    /// d'efficacité, caches L3) et poser une préférence de cœurs sur un processus par CPU Sets.
    /// Non compilée au banc d'essai : tout ce qui décide est dans Topologie.cs.
    /// </summary>
    internal static partial class Topologie
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetLogicalProcessorInformationEx(int relation, IntPtr buffer, ref uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemCpuSetInformation(IntPtr information, uint bufferLength, out uint returnedLength, IntPtr process, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessDefaultCpuSets(IntPtr process, uint[] cpuSetIds, uint cpuSetIdCount);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr h);

        private const int RelationProcessorCore = 0, RelationCache = 2;
        private const uint ProcessSetLimitedInformation = 0x2000;

        private static readonly object _verrou = new object();
        private static List<Proc> _cache;

        /// <summary>Lit le processeur une fois par lancement. Vide si la lecture échoue ou si la
        /// machine a plusieurs groupes de processeurs (plus de 64 logiques).</summary>
        public static List<Proc> Lit()
        {
            lock (_verrou)
            {
                if (_cache == null)
                {
                    try { _cache = LitMachine(); }
                    catch (Exception ex) { JournalTechnique.Echec("Topologie.Lit", ex); _cache = new List<Proc>(); }
                }
                return new List<Proc>(_cache);
            }
        }

        /// <summary>Parcourt les enregistrements de GetLogicalProcessorInformationEx pour une relation.</summary>
        private static List<byte[]> Enregistrements(int relation)
        {
            var res = new List<byte[]>();
            uint len = 0;
            GetLogicalProcessorInformationEx(relation, IntPtr.Zero, ref len);
            if (len == 0) return res;
            IntPtr buf = Marshal.AllocHGlobal((int)len);
            try
            {
                if (!GetLogicalProcessorInformationEx(relation, buf, ref len)) return res;
                int off = 0;
                while (off + 8 <= len)
                {
                    int taille = Marshal.ReadInt32(buf, off + 4);
                    if (taille <= 0 || off + taille > len) break;
                    var b = new byte[taille];
                    Marshal.Copy(buf + off, b, 0, taille);
                    res.Add(b);
                    off += taille;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return res;
        }

        /// <summary>Lit les GROUP_AFFINITY d'un enregistrement. Rend false si un groupe ≠ 0.</summary>
        private static bool Masques(byte[] b, int offCompte, int offMasques, out ulong masque)
        {
            masque = 0;
            int pas = IntPtr.Size + 8;   // KAFFINITY + WORD Group + WORD Reserved[3]
            int n = BitConverter.ToUInt16(b, offCompte);
            if (n == 0) n = 1;   // anciens Windows : un seul GroupMask, compteur réservé à 0
            for (int i = 0; i < n; i++)
            {
                int o = offMasques + i * pas;
                if (o + pas > b.Length) return false;
                ulong m = IntPtr.Size == 8 ? BitConverter.ToUInt64(b, o) : BitConverter.ToUInt32(b, o);
                int groupe = BitConverter.ToUInt16(b, o + IntPtr.Size);
                if (groupe != 0 && m != 0) return false;
                masque |= m;
            }
            return true;
        }

        private static List<Proc> LitMachine()
        {
            var vide = new List<Proc>();
            var procs = new Dictionary<int, Proc>();

            // PROCESSOR_RELATIONSHIP : Flags(8) EfficiencyClass(9) Reserved[20] GroupCount(30) GroupMask[](32)
            int coeur = 0;
            foreach (byte[] b in Enregistrements(RelationProcessorCore))
            {
                if (b.Length < 32 + IntPtr.Size) continue;
                ulong m;
                if (!Masques(b, 30, 32, out m)) return vide;
                for (int i = 0; i < 64; i++)
                    if ((m & (1UL << i)) != 0) procs[i] = new Proc { Index = i, Coeur = coeur, Efficacite = b[9] };
                coeur++;
            }
            if (procs.Count == 0) return vide;

            // CACHE_RELATIONSHIP : Level(8) … CacheSize(12) Type(16) Reserved[18] GroupCount(38) GroupMask[](40)
            int l3 = 0;
            foreach (byte[] b in Enregistrements(RelationCache))
            {
                if (b.Length < 40 + IntPtr.Size || b[8] != 3) continue;
                ulong m;
                if (!Masques(b, 38, 40, out m)) return vide;
                long taille = BitConverter.ToUInt32(b, 12);
                foreach (Proc p in procs.Values)
                    if ((m & (1UL << p.Index)) != 0) { p.L3 = l3; p.L3Octets = taille; }
                l3++;
            }
            return procs.Values.OrderBy(p => p.Index).ToList();
        }

        /// <summary>Index logique (groupe 0) → identifiant de CPU Set.</summary>
        private static Dictionary<int, uint> IdsCpuSets()
        {
            var d = new Dictionary<int, uint>();
            uint len;
            GetSystemCpuSetInformation(IntPtr.Zero, 0, out len, IntPtr.Zero, 0);
            if (len == 0) return d;
            IntPtr buf = Marshal.AllocHGlobal((int)len);
            try
            {
                if (!GetSystemCpuSetInformation(buf, len, out len, IntPtr.Zero, 0)) return d;
                int off = 0;
                // SYSTEM_CPU_SET_INFORMATION : Size(0) Type(4) Id(8) Group(12) LogicalProcessorIndex(14)
                while (off + 16 <= len)
                {
                    int taille = Marshal.ReadInt32(buf, off);
                    if (taille <= 0 || off + taille > len) break;
                    uint id = (uint)Marshal.ReadInt32(buf, off + 8);
                    int groupe = Marshal.ReadInt16(buf, off + 12);
                    int index = Marshal.ReadByte(buf, off + 14);
                    if (groupe == 0) d[index] = id;
                    off += taille;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return d;
        }

        /// <summary>
        /// Pose une préférence de cœurs sur le processus. Indices vides = retirer la préférence.
        /// En cas de refus (anti-triche, processus protégé) : journal, et RIEN d'autre — pas
        /// d'affinité stricte en repli.
        /// </summary>
        public static bool AppliqueCpuSets(int pid, IList<int> indices, Action<string, int> log)
        {
            string nom = "processus " + pid;
            try { nom = System.Diagnostics.Process.GetProcessById(pid).ProcessName; } catch { }
            uint[] ids = null;
            if (indices != null && indices.Count > 0)
            {
                Dictionary<int, uint> carte = IdsCpuSets();
                if (indices.Any(i => !carte.ContainsKey(i)))
                {
                    if (log != null) log("CPU Sets introuvables pour " + nom + " : jeu laissé tel quel.", 2);
                    return false;
                }
                ids = indices.Select(i => carte[i]).ToArray();
            }
            IntPtr h = OpenProcess(ProcessSetLimitedInformation, false, pid);
            if (h == IntPtr.Zero)
            {
                if (log != null) log("CPU Sets refusés pour " + nom + " (code " + Marshal.GetLastWin32Error() + ") : jeu laissé tel quel.", 2);
                return false;
            }
            try
            {
                if (SetProcessDefaultCpuSets(h, ids, ids == null ? 0u : (uint)ids.Length)) return true;
                if (log != null) log("CPU Sets refusés pour " + nom + " (code " + Marshal.GetLastWin32Error() + ") : jeu laissé tel quel.", 2);
                return false;
            }
            finally { CloseHandle(h); }
        }
    }
}
