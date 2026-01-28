using MiniApp.ViewModels;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace MiniApp.Helpers
{
    public static class MiniAppSessionManager
    {
        private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniApp", "miniapp_session.dat");

        public static void Save(MiniAppSession session)
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

        public static MiniAppSession? Load()
        {
            if (!File.Exists(FilePath)) return null;

            try
            {
                var encrypted = File.ReadAllBytes(FilePath);

                var bytes = ProtectedData.Unprotect(
                    encrypted, null, DataProtectionScope.CurrentUser);

                var json = Encoding.UTF8.GetString(bytes);
                return JsonConvert.DeserializeObject<MiniAppSession>(json);
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
