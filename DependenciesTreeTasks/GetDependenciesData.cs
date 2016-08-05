using System;
using System.Collections.Generic;
using Microsoft.Build.Framework;
using System.Linq;
using Microsoft.Build.Utilities;
using System.IO;

namespace DependenciesTreeTasks
{
     public class GetDependenciesData : Microsoft.Build.Utilities.Task
    {
        [Required]
        public ITaskItem[] TargetDefinitions { get; set; }

        [Required]
        public ITaskItem[] PackageDefinitions { get; set; }

        [Required]
        public ITaskItem[] FileDefinitions { get; set; }

        [Required]
        public ITaskItem[] PackageDependencies { get; set; }

        [Required]
        public ITaskItem[] FileDependencies { get; set; }

        [Output]
        public ITaskItem[] DependenciesWorld { get; set; }

        public override bool Execute()
        {
            // populate unique targets
            var targets = new Dictionary<string, DependencyMetadata>(StringComparer.OrdinalIgnoreCase);
            var dependencies = new Dictionary<string, DependencyMetadata>(StringComparer.OrdinalIgnoreCase);
            var files = new Dictionary<string, DependencyMetadata>(StringComparer.OrdinalIgnoreCase);

            foreach (var targetDef in TargetDefinitions)
            {
                if (targetDef.ItemSpec.Contains("/"))
                {
                    // skip "target/rid"s and only consume actuall targets
                    continue;
                }

                var target = new DependencyMetadata(runtimeIdentifier: targetDef.GetMetadata("RuntimeIdentifier"),
                                                    targetFrameworkMoniker: targetDef.GetMetadata("TargetFramework"),
                                                    frameworkName: targetDef.GetMetadata("FrameworkName"),
                                                    frameworkVersion: targetDef.GetMetadata("FrameworkVersion"),
                                                    type: "Target");
                targets[targetDef.ItemSpec] = target;
            }

            // populate unique packages 
            foreach(var packageDef in PackageDefinitions)
            {
                // TODO How to determine if package is resolved or not?
                // TODO Paths are unresolved
                // TODO diagnostics 

                var dependencyType = packageDef.GetMetadata("Type");
                if (string.IsNullOrEmpty(dependencyType))
                {
                    dependencyType = "Package";
                }

                var dependency = new DependencyMetadata(name: packageDef.GetMetadata("Name"),
                                                    version: packageDef.GetMetadata("Version"),
                                                    type: dependencyType,
                                                    path: packageDef.GetMetadata("Path"));
                dependencies[packageDef.ItemSpec] = dependency;
            }

            // populate unique files 
            foreach (var fileDef in FileDefinitions)
            {
                // TODO How to determine a framework assembly
                var dependencyType = fileDef.GetMetadata("Type");
                if (string.IsNullOrEmpty(dependencyType))
                {
                    dependencyType = "Assembly";
                }

                var name = Path.GetFileName(fileDef.ItemSpec);
                var assembly = new DependencyMetadata(name: name,
                                                      type: dependencyType,
                                                      path: fileDef.GetMetadata("Path"));
                files[fileDef.ItemSpec] = assembly;
            }

            var dependenciesWorld = new Dictionary<string, DependencyMetadata>(StringComparer.OrdinalIgnoreCase);

            // populate package dependencies 
            foreach (var packageDependency in PackageDependencies)
            {
                var currentPackageId = packageDependency.ItemSpec;
                var parentTargetId = packageDependency.GetMetadata("ParentTarget");
                if (parentTargetId.Contains("/"))
                {
                    // skip "target/rid"s and only consume actual targets
                    continue;
                }

                // add package to the world

                var parentPackageId = packageDependency.GetMetadata("ParentPackage");
                var currentPackageUniqueId = $"{parentTargetId}/{currentPackageId}";                
                DependencyMetadata currentPackageDependency = null;
                if (!dependenciesWorld.TryGetValue(currentPackageUniqueId, out currentPackageDependency))
                {
                    if (dependencies.Keys.Contains(currentPackageId))
                    {
                        // add current package to dependencies world
                        dependenciesWorld[currentPackageUniqueId] = dependencies[currentPackageId];
                    }
                }

                // update parent
                var parentDependencyId = $"{parentTargetId}/{parentPackageId}".Trim('/'); ;
                DependencyMetadata parentDependency = null;
                if (dependenciesWorld.TryGetValue(parentDependencyId, out parentDependency))
                {
                 
                    parentDependency.Dependencies.Add(currentPackageId);
                }
                else
                {
                    // create new parent
                    if (!string.IsNullOrEmpty(parentPackageId))
                    {
                        parentDependency = dependencies[parentPackageId];
                    }
                    else
                    {
                        parentDependency = targets[parentTargetId];
                    }

                    if (parentDependency == null)
                    {
                        continue;
                    }

                    parentDependency.Dependencies.Add(currentPackageId);
                    dependenciesWorld[parentDependencyId] = parentDependency;
                }
            }

            // populate assembly dependencies 
            foreach (var assemblyDependency in FileDependencies)
            {
                var currentAssemblyId = assemblyDependency.ItemSpec;
                var fileGroup = assemblyDependency.GetMetadata("FileGroup");
                if (string.IsNullOrEmpty(fileGroup) 
                    || !fileGroup.Equals("CompileTimeAssembly")
                    || currentAssemblyId.EndsWith("_._"))
                {
                    continue;
                }

                var parentTargetId = assemblyDependency.GetMetadata("ParentTarget");
                if (parentTargetId.Contains("/"))
                {
                    // skip "target/rid"s and only consume actual targets
                    continue;
                }

                // add package to the world
                var parentPackageId = assemblyDependency.GetMetadata("ParentPackage");
                var currentAssemblyUniqueId = $"{parentTargetId}/{currentAssemblyId}";
                DependencyMetadata currentDependency = null;
                if (!dependenciesWorld.TryGetValue(currentAssemblyUniqueId, out currentDependency))
                {
                    if (files.Keys.Contains(currentAssemblyId))
                    {
                        // add current package to dependencies world
                        dependenciesWorld[currentAssemblyUniqueId] = files[currentAssemblyId];
                    }
                }

                // update parent
                var parentDependencyId = $"{parentTargetId}/{parentPackageId}".Trim('/'); ;
                DependencyMetadata parentDependency = null;
                if (dependenciesWorld.TryGetValue(parentDependencyId, out parentDependency))
                {
                    parentDependency.Dependencies.Add(currentAssemblyId);
                }
                else
                {
                    // create new parent
                    if (!string.IsNullOrEmpty(parentPackageId))
                    {
                        parentDependency = dependencies[parentPackageId];
                    }
                    else
                    {
                        parentDependency = targets[parentTargetId];
                    }

                    if (parentDependency == null)
                    {
                        continue;
                    }

                    parentDependency.Dependencies.Add(currentAssemblyId);
                    dependenciesWorld[parentDependencyId] = parentDependency;
                }
            }

            // Test data 
            //var myAssembly1Dependency = new DependencyMetadata("MyAssembly1.dll", "1.0.0.0", "Assembly", "c/temp/MyPackage/MyAssembly1.dll");
            //var myAssembly2Dependency = new DependencyMetadata("MyAssembly2.dll", "2.0.0.0", "Assembly", "c/temp/MyPackage/MyAssembly2.dll");
            //var myGacAssembly1Dependency = new DependencyMetadata("MyFrameworkAssembly1.dll", "1.0.0.0", "FrameworkAssembly", "c/temp/MyPackage/MyGacAssembly1.dll");
            //var myGacAssembly2Dependency = new DependencyMetadata("MyFrameworkAssembly2.dll", "2.0.0.0", "FrameworkAssembly", "c/temp/MyPackage/MyGacAssembly2.dll");

            //var myPackageDependency2 = new DependencyMetadata("MyPackage2", "1.0.0.0", "Package", "c/temp/MyPackage2");
            //var myAssembly3Dependency = new DependencyMetadata("MyAssembly3.dll", "2.0.0.0", "Assembly", "c/temp/MyPackage/MyAssembly3.dll");
            //var myGacAssembly3Dependency = new DependencyMetadata("MyFrameworkAssembly3.dll", "1.0.0.0", "FrameworkAssembly", "c/temp/MyPackage/MyGacAssembly3.dll");
            //myPackageDependency2.Dependencies.Add("MyAssembly3.dll/1.0.0.0/c/temp/MyPackage");
            //myPackageDependency2.Dependencies.Add("MyFrameworkAssembly3.dll/1.0.0.0/c/temp/MyPackage");

            //var myPackageDependency = new DependencyMetadata("MyPackage", "1.0.0.0", "Package", "c/temp/MyPackage");
            //myPackageDependency.Dependencies.Add("MyPackage2/1.0.0.0");
            //myPackageDependency.Dependencies.Add("MyAssembly1.dll/1.0.0.0/c/temp/MyPackage");
            //myPackageDependency.Dependencies.Add("MyAssembly2.dll/2.0.0.0/c/temp/MyPackage");
            //myPackageDependency.Dependencies.Add("MyFrameworkAssembly1.dll/1.0.0.0/c/temp/MyPackage");
            //myPackageDependency.Dependencies.Add("MyFrameworkAssembly2.dll/2.0.0.0/c/temp/MyPackage");

            //dependenciesWorld[".NETFramework,Version=v4.6"].Dependencies.Add("MyPackage/1.0.0.0");
            //dependenciesWorld[".NETFramework,Version=v4.6"].Dependencies.Add("MyPackage2/1.0.0.0");

            //dependenciesWorld.Add(".NETFramework,Version=v4.6/MyPackage/1.0.0.0", myPackageDependency);
            //dependenciesWorld.Add(".NETFramework,Version=v4.6/MyPackage2/1.0.0.0", myPackageDependency2);
            //dependenciesWorld.Add(".NETFramework,Version=v4.6/MyAssembly1.dll/1.0.0.0/c/temp/MyPackage", myAssembly1Dependency);
            //dependenciesWorld.Add(".NETFramework,Version=v4.6/MyAssembly2.dll/2.0.0.0/c/temp/MyPackage", myAssembly2Dependency);
            //dependenciesWorld.Add(".NETFramework,Version=v4.6/MyFrameworkAssembly1.dll/1.0.0.0/c/temp/MyPackage", myGacAssembly1Dependency);
            //dependenciesWorld.Add(".NETFramework,Version=v4.6/MyFrameworkAssembly2.dll/2.0.0.0/c/temp/MyPackage", myGacAssembly2Dependency);
            //dependenciesWorld.Add(".NETFramework,Version=v4.6/MyAssembly3.dll/1.0.0.0/c/temp/MyPackage", myAssembly3Dependency);
            //dependenciesWorld.Add(".NETFramework,Version=v4.6/MyFrameworkAssembly3.dll/1.0.0.0/c/temp/MyPackage", myGacAssembly3Dependency);

            DependenciesWorld = dependenciesWorld.Select(kvp =>
                {
                    var newTaskItem = new TaskItem(kvp.Key);
                    newTaskItem.SetMetadata("RuntimeIdentifier", kvp.Value.RuntimeIdentifier);
                    newTaskItem.SetMetadata("TargetFramework", kvp.Value.TargetFrameworkMoniker);
                    newTaskItem.SetMetadata("FrameworkName", kvp.Value.FrameworkName);
                    newTaskItem.SetMetadata("FrameworkVersion", kvp.Value.FrameworkVersion);
                    newTaskItem.SetMetadata("Name", kvp.Value.Name);
                    newTaskItem.SetMetadata("Version", kvp.Value.Version);
                    newTaskItem.SetMetadata("DependencyType", kvp.Value.DependencyType);
                    newTaskItem.SetMetadata("Path", kvp.Value.Path);
                    newTaskItem.SetMetadata("Dependencies", string.Join(";", kvp.Value.Dependencies));

                    return newTaskItem;
                }).ToArray();

            return true;
        }


        private class DependencyMetadata
        {
            public DependencyMetadata(string name = null,
                                      string version = null,
                                      string type = null,
                                      string path = null,
                                      string runtimeIdentifier = null,
                                      string targetFrameworkMoniker = null,
                                      string frameworkName = null,
                                      string frameworkVersion = null)
            {
                Name = name ?? string.Empty;
                Version = version ?? string.Empty;
                DependencyType = type ?? string.Empty;
                Path = path ?? string.Empty;
                RuntimeIdentifier = runtimeIdentifier ?? string.Empty;
                TargetFrameworkMoniker = targetFrameworkMoniker ?? string.Empty;
                FrameworkName = frameworkName ?? string.Empty;
                FrameworkVersion = frameworkVersion ?? string.Empty;

                Dependencies = new List<string>();
            }

            // dependency properties
            public string Name { get; private set; }
            public string Version { get; private set; }
            public string DependencyType { get; private set; }
            public string Path { get; private set; }

            // target framework properties
            public string RuntimeIdentifier { get; private set; }
            public string TargetFrameworkMoniker { get; private set; }
            public string FrameworkName { get; private set; }
            public string FrameworkVersion { get; private set; }

            // TODO Add diagnostics properties

            /// <summary>
            /// a list of name/version strings to specify dependencies identities
            /// </summary>
            public IList<string> Dependencies { get; private set; }
        }
    }
}