using kuboPix.ViewModels;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace kuboPix.Helpers
{
    public static class kuboPixSessionManager
    {
        private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "kuboPix", "kuboPix_session.dat");

        public static void Save(kuboPixSession session)
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (directory is not null)
            {
                _ = Directory.CreateDirectory(directory);
            }

            var json = JsonConvert.SerializeObject(session);
            var bytes = Encoding.UTF8.GetBytes(json);

            var encrypted = ProtectedData.Protect(
                bytes, null, DataProtectionScope.CurrentUser);

            File.WriteAllBytes(FilePath, encrypted);
        }

        public static kuboPixSession? Load()
        {
            if (!File.Exists(FilePath)) return null;

            try
            {
                var encrypted = File.ReadAllBytes(FilePath);

                var bytes = ProtectedData.Unprotect(
                    encrypted, null, DataProtectionScope.CurrentUser);

                var json = Encoding.UTF8.GetString(bytes);
                return JsonConvert.DeserializeObject<kuboPixSession>(json);
            }
            catch
            {
                return null;
            }
        }

        public static void Clear()
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
    }
}
