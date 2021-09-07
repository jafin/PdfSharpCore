using System.IO;
using System.Reflection;

namespace PdfSharpCore.UnitTests.Helpers
{
    public class PathHelper
    {
        private readonly string _rootDir;

        public PathHelper()
        {
            _rootDir = Path.GetDirectoryName(GetType().GetTypeInfo().Assembly.Location);
        }

        public string GetAssetPath(string name)
        {
            return Path.Combine(_rootDir, "Assets", name);
        }

        public string RootDir => _rootDir;

        public static PathHelper GetInstance()
        {
            if (_instance == null)
                _instance = new PathHelper();
            return _instance;
        }

        private static PathHelper _instance;
    }
}