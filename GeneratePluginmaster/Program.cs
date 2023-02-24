using Dalamud.Plugin.Internal.Types;
using Newtonsoft.Json;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal class Program
{
    const string BaseUrl = "https://love.puni.sh/plugins/";
    private static void Main(string[] args)
    {
        if(args.Length == 0)
        {
            Console.WriteLine($"No arguments specified, assuming current folder contains plugins folder");
            Process(".");
        }
        else
        {
            Process(args[0]);
        }
    }

    static void Process(string folderPath)
    {
        var path = Path.Combine(folderPath, $"plugins");
        Console.WriteLine($"Plugins folder: {path}");
        if (!Directory.Exists(path))
        {
            Console.WriteLine($"Folder does not exists");
            Environment.Exit(0);
        }
        List<PluginManifest> Manifests = new();
        foreach(var f in Directory.GetDirectories(path))
        {
            var m = ProcessPlugin(f);
            if(m != null)
            {
                Manifests.Add(m);
            }
        }
        var mf = JsonConvert.SerializeObject(Manifests, Formatting.Indented, new JsonSerializerSettings()
        {
            DefaultValueHandling = DefaultValueHandling.Ignore,
        });
        var p = Path.Combine(folderPath, $"ment.json");
        Console.WriteLine($"Writing to: {p}");
        File.WriteAllText(p, mf);
    }

    static PluginManifest? ProcessPlugin(string path)
    {
        Console.WriteLine($"Processing {path}");
        if (!Directory.Exists(path))
        {
            Console.WriteLine("Folder doesn't exists");
            return null;
        }
        var file = Path.Combine(path, "latest.zip");
        var testing = Path.Combine(path, "Testing", "latest.zip");
        if (!File.Exists(file))
        {
            Console.WriteLine("Can't find latest.zip");
            return null;
        }
        try
        {
            var manifest = GetMainfest(file);
            if(manifest == null)
            {
                Console.WriteLine("Could not get plugin manifest");
                return null;
            }
            manifest.DownloadLinkInstall = $"{BaseUrl}{new DirectoryInfo(path).Name}/latest.zip";
            manifest.DownloadLinkUpdate = manifest.DownloadLinkInstall;
            manifest.DownloadLinkTesting = manifest.DownloadLinkInstall;
            if (File.Exists(testing))
            {
                Console.WriteLine("Now processing testing version");
                var testingManifest = GetMainfest(testing);
                if(testingManifest != null)
                {
                    manifest.DownloadLinkTesting = $"{BaseUrl}{new DirectoryInfo(path).Name}/Testing/latest.zip";
                    manifest.TestingAssemblyVersion = testingManifest.AssemblyVersion;
                }
                else
                {
                    Console.WriteLine("Could not get testing plugin manifest");
                    manifest.DownloadLinkTesting = manifest.DownloadLinkInstall;
                }
            }
            else
            {
                Console.WriteLine("No testing version for this plugin");
            }
            return manifest;
        }
        catch(Exception e)
        {
            Console.WriteLine($"{e.Message}\n{e.StackTrace}");
        }
        return null;
    }

    static PluginManifest? GetMainfest(string file)
    {
        using ZipArchive zip = ZipFile.OpenRead(file);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (entry.FullName.EndsWith(".dll"))
            {
                try
                {
                    Console.WriteLine($"Inspecting {entry.FullName}");
                    using var sr = new MemoryStream(ReadStream(entry.Open()));
                    using var portableExecutableReader = new PEReader(sr);
                    var metadataReader = portableExecutableReader.GetMetadataReader();

                    foreach (var typeDefHandle in metadataReader.TypeDefinitions)
                    {
                        var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
                        var name = metadataReader.GetString(typeDef.Name);
                        //Console.WriteLine($"Checking {name}");
                        var interfaceHandles = typeDef.GetInterfaceImplementations();

                        foreach (var interfaceHandle in interfaceHandles)
                        {
                            var interfaceImplementation = metadataReader.GetInterfaceImplementation(interfaceHandle);
                            var interfaceType = interfaceImplementation.Interface;

                            if (interfaceType.Kind == HandleKind.TypeReference)
                            {
                                var typeReferenceHandle = (TypeReferenceHandle)interfaceType;
                                var typeReference = metadataReader.GetTypeReference(typeReferenceHandle);
                                var namespaceName = metadataReader.GetString(typeReference.Namespace);
                                var interfaceName = metadataReader.GetString(typeReference.Name);
                                if ($"{namespaceName}.{interfaceName}" == "Dalamud.Plugin.IDalamudPlugin")
                                {
                                    var jsonF = $"{entry.FullName[..^4]}.json";
                                    Console.WriteLine($"Found plugin: {entry.FullName}, loading {jsonF}");
                                    using var reader = new StreamReader(zip.Entries.First(x => x.FullName == jsonF).Open());
                                    var json = JsonConvert.DeserializeObject<PluginManifest>(reader.ReadToEnd());
                                    return json;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Can't load {entry.FullName}: {ex.Message}");
                }
            }
        }
        return null;
    }

    static byte[] ReadStream(Stream sourceStream)
    {
        using (var memoryStream = new MemoryStream())
        {
            sourceStream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }
    }
}