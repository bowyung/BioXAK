using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BioSAK.Models;

namespace BioSAK.Services
{
  
    public static class VectorLibrary
    {
        private static List<VectorTemplate> _templates;
        private static readonly object _lock = new object();

        public static List<VectorTemplate> GetAllTemplates()
        {
            lock (_lock)
            {
                if (_templates == null)
                    _templates = LoadAllTemplates();
                return _templates;
            }
        }

        public static void Reload()
        {
            lock (_lock) { _templates = null; }
        }

        public static List<string> GetCategories()
        {
            return GetAllTemplates()
                .Select(t => t.Category).Distinct().OrderBy(c => c).ToList();
        }

        public static List<VectorTemplate> GetByCategory(string category)
        {
            return GetAllTemplates()
                .Where(t => t.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public static VectorTemplate GetByName(string name)
        {
            return GetAllTemplates()
                .FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private static List<VectorTemplate> LoadAllTemplates()
        {
            var templates = new List<VectorTemplate>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. 嵌入資源
            try
            {
                foreach (var t in GenBankParser.ParseAllFromEmbeddedResources())
                    if (names.Add(t.Name)) templates.Add(t);
            }
            catch { /* 靜默 */ }

            // 2. 外部目錄
            try
            {
                string dir = GetVectorDirectory();
                if (Directory.Exists(dir))
                    foreach (var t in GenBankParser.ParseAllFromDirectory(dir))
                        if (names.Add(t.Name)) templates.Add(t);
            }
            catch { /* 靜默 */ }

            return templates.OrderBy(t => t.Category).ThenBy(t => t.Name).ToList();
        }

        private static string GetVectorDirectory()
        {
            var paths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Vectors"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Vectors"),
                Path.Combine(Environment.CurrentDirectory, "Data", "Vectors"),
            };
            return paths.FirstOrDefault(Directory.Exists) ?? paths[0];
        }
    }
}
