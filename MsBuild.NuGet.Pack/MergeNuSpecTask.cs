﻿namespace MsBuild.NuGet.Pack
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Xml.Linq;
    using Microsoft.Build.Framework;
    using Microsoft.Build.Utilities;

    /// <summary>
    ///     The merge nu spec task.
    /// </summary>
    public class MergeNuSpecTask : NuSpecTask
    {
        /// <summary>
        ///     Executes a task.
        /// </summary>
        /// <returns>
        ///     true if the task executed successfully; otherwise, false.
        /// </returns>
        /// <inheritdoc />
        public override bool Execute()
        {
            try
            {
                var packageConfig = Path.Combine(ProjectDirectory, "packages.config");

                var nuSpecDocument = OpenXmlDocument(NuSpecPath);

                MergeMetadata(nuSpecDocument, PrimaryOutputAssembly);

                if (File.Exists(packageConfig))
                {
                    MergePackageDependencies(nuSpecDocument, packageConfig);
                }

                var projectPath = Path.Combine(ProjectDirectory, ProjectFile);

                MergeReferences(nuSpecDocument, projectPath);

                MergeFile(nuSpecDocument, PrimaryOutputAssembly);

                nuSpecDocument.Save(NuSpecPath);
                //return true;
            }
            catch (Exception e)
            {
                Log.LogError(e.Message);
                //LogMessage(e.Message, MessageImportance.High);
                //return false;
            }
            return !Log.HasLoggedErrors;

        }

        /// <summary>
        ///     Gets the current user.
        /// </summary>
        /// <returns>
        ///     The <see cref="string" />.
        /// </returns>
        private static string GetCurrentUser()
        {
            var fallbackUserName = Environment.UserName;

            if (Thread.CurrentPrincipal == null)
            {
                return fallbackUserName;
            }

            if (Thread.CurrentPrincipal.Identity == null)
            {
                return fallbackUserName;
            }

            var currentUser = Thread.CurrentPrincipal.Identity.Name;

            if (string.IsNullOrWhiteSpace(currentUser))
            {
                return fallbackUserName;
            }

            return currentUser;
        }

        /// <summary>
        /// Gets the project packages.
        /// </summary>
        /// <param name="packageConfig">
        /// The package configuration.
        /// </param>
        /// <returns>
        /// The <see cref="IEnumerable{T}"/>.
        /// </returns>
        private static IEnumerable<XElement> GetProjectPackages(string packageConfig)
        {
            var document = OpenXmlDocument(packageConfig);
            var root = GetElement(document, "packages");

            var packages =
                root.Elements()
                    .Where(
                        x =>
                            x.Name.LocalName == "package" &&
                            (x.Attribute("developmentDependency") == null ||
                             x.Attribute("developmentDependency").Value != "true"));


            return packages;
        }

        /// <summary>
        /// Gets the project references.
        /// </summary>
        /// <param name="projectPath">
        /// The project path.
        /// </param>
        /// <returns>
        /// The <see cref="IEnumerable{T}"/>.
        /// </returns>
        private static IEnumerable<XElement> GetProjectReferences(string projectPath)
        {
            var projectDocument = OpenXmlDocument(projectPath);
            var defaultNamespace = projectDocument.Root.GetDefaultNamespace();
            var references = projectDocument.Descendants(defaultNamespace + "Reference").ToList();

            foreach (var referenceElement in references)
            {
                if (referenceElement.Attribute("Include").Value.StartsWith("System.") == false)
                {
                    continue;
                }

                yield return referenceElement;
            }
        }

        /// <summary>
        /// Gets the spec dependencies.
        /// </summary>
        /// <param name="nuSpecDocument">
        /// The nu spec document.
        /// </param>
        /// <returns>
        /// The <see cref="XElement"/>.
        /// </returns>
        private static XElement GetSpecDependencies(XDocument nuSpecDocument)
        {
            var metadata = GetSpecMetadata(nuSpecDocument);

            var dependencies = GetOrCreateElement(metadata, "dependencies");

            return dependencies;
        }

        /// <summary>
        /// Gets the spec references.
        /// </summary>
        /// <param name="nuSpecDocument">
        /// The nu spec document.
        /// </param>
        /// <returns>
        /// The <see cref="XElement"/>.
        /// </returns>
        private static XElement GetSpecReferences(XDocument nuSpecDocument)
        {
            var metadata = GetSpecMetadata(nuSpecDocument);

            var frameworkAssemblies = GetOrCreateElement(metadata, "frameworkAssemblies");

            return frameworkAssemblies;
        }

        /// <summary>
        /// Gets the target framework.
        /// </summary>
        /// <param name="targetFrameworkVersion">
        /// The target framework version.
        /// </param>
        /// <param name="targetFrameworkProfile">
        /// </param>
        /// <returns>
        /// The nuget framework target.
        /// </returns>
        private static string GetTargetFramework(string targetFrameworkVersion, string targetFrameworkProfile)
        {
            if (targetFrameworkVersion == "v2.0")
            {
                return "net20";
            }

            if (targetFrameworkVersion == "v3.0")
            {
                return "net30";
            }

            if (targetFrameworkVersion == "v3.5")
            {
                if (string.Equals(targetFrameworkProfile, "client", StringComparison.OrdinalIgnoreCase))
                {
                    return "net35-client";
                }

                return "net35";
            }

            if (targetFrameworkVersion == "v4.0")
            {
                if (string.Equals(targetFrameworkProfile, "client", StringComparison.OrdinalIgnoreCase))
                {
                    return "net40-client";
                }

                return "net40";
            }

            if (targetFrameworkVersion == "v4.5")
            {
                return "net45";
            }

            if (targetFrameworkVersion == "v4.5.1")
            {
                return "net451";
            }

            return string.Empty;
        }

        /// <summary>
        /// Sets the element value.
        /// </summary>
        /// <param name="parent">
        /// The parent.
        /// </param>
        /// <param name="elementName">
        /// Name of the element.
        /// </param>
        /// <param name="value">
        /// The value.
        /// </param>
        private static void SetElementValue(XElement parent, string elementName, string value)
        {
            var element = GetOrCreateElement(parent, elementName);

            element.Value = value;
        }

        /// <summary>
        /// Sets the element value if empty.
        /// </summary>
        /// <param name="parent">
        /// The parent.
        /// </param>
        /// <param name="elementName">
        /// Name of the element.
        /// </param>
        /// <param name="value">
        /// The value.
        /// </param>
        private static void SetElementValueIfEmpty(XElement parent, string elementName, string value)
        {
            var element = GetOrCreateElement(parent, elementName);

            if (string.IsNullOrWhiteSpace(element.Value))
            {
                element.Value = value;
            }
        }

        /// <summary>
        /// Merges the file.
        /// </summary>
        /// <param name="document">
        /// The document.
        /// </param>
        /// <param name="primaryOutputAssembly">
        /// The primary output assembly.
        /// </param>
        private void MergeFile(XDocument document, string primaryOutputAssembly)
        {
            LogMessage("Merging file reference from project output");

            var package = GetElement(document, "package");
            var files = GetElement(package, "files");
            var defaultNamespace = document.Root.GetDefaultNamespace();
            var outputName = Path.GetFileNameWithoutExtension(primaryOutputAssembly);
            var srcValue = "**\\" + outputName + ".*";
            var targetFramework = GetTargetFramework(TargetFrameworkVersion, TargetFrameworkProfile);
            var frameworkFolder = string.Empty;

            if (string.IsNullOrWhiteSpace(targetFramework) == false)
            {
                frameworkFolder = "\\" + targetFramework;
            }

            files.Add(
                new XElement(
                    defaultNamespace + "file",
                    new XAttribute("src", srcValue),
                    new XAttribute("target", "lib" + frameworkFolder),
                    new XAttribute("exclude", FileExclusionPattern)));
        }

        /// <summary>
        /// Merges the version.
        /// </summary>
        /// <param name="nuSpecDocument">
        /// The nu spec document.
        /// </param>
        /// <param name="assemblyPath">
        /// The assembly path.
        /// </param>
        private void MergeMetadata(XDocument nuSpecDocument, string assemblyPath)
        {
            LogMessage("Merging metadata from " + assemblyPath);

            var info = FileVersionInfo.GetVersionInfo(assemblyPath);
            var metadata = GetSpecMetadata(nuSpecDocument);

            SetElementValue(metadata, "title", info.ProductName);

            string version;
            bool isValidProductVersion = IsValidVersion(info.ProductVersion);

            if (OverrideNuGetVersion && !IsValidVersion(NuGetVersion))
            {
                throw new InvalidOperationException(string.Format("OverrideNuGetVersion set to true but NuGetVersion invalid. NuGetVersion was: {0}", NuGetVersion));
            }
 
            if (OverrideNuGetVersion)
            {
                version = NuGetVersion;
            }
            else if (IncludeBuildVersion)
            {
                if (isValidProductVersion)
                {
                    version = info.ProductVersion;
                }
                else
                {
                    version = info.FileVersion;
                }
            }
            else if (UseBuildVersionAsPatch)
            {
                if (isValidProductVersion)
                {
                    version = info.ProductMajorPart + "." + info.ProductMinorPart + "." + info.ProductPrivatePart;
                }
                else
                {
                    version = info.FileMajorPart + "." + info.FileMinorPart + "." + info.FilePrivatePart;
                }
            }
            else
            {
                if (isValidProductVersion)
                {
                    version = info.ProductMajorPart + "." + info.ProductMinorPart + "." + info.ProductBuildPart;
                }
                else
                {
                    version = info.FileMajorPart + "." + info.FileMinorPart + "." + info.FileBuildPart;
                }
            }

            LogMessage("Using version {0} from {1}", MessageImportance.Low, version, assemblyPath);

            SetElementValue(metadata, "version", version);
            SetElementValueIfEmpty(metadata, "summary", info.Comments);
            SetElementValueIfEmpty(metadata, "description", info.FileDescription);

            var currentUser = GetCurrentUser();

            SetElementValueIfEmpty(metadata, "authors", currentUser);
            SetElementValueIfEmpty(metadata, "owners", currentUser);
        }

        private static bool IsValidVersion(string version)
        {
            return Regex.IsMatch(version, @"^\d+.\d+.\d+(-([a-zA-Z0-9-]+))?$"); //semver v1.0 http://semver.org/spec/v1.0.0.html
        }

        /// <summary>
        /// Merges the package dependencies.
        /// </summary>
        /// <param name="nuSpecDocument">
        /// The nu spec document.
        /// </param>
        /// <param name="packageConfig">
        /// The package configuration.
        /// </param>
        private void MergePackageDependencies(XDocument nuSpecDocument, string packageConfig)
        {
            LogMessage("Merging package dependencies from " + packageConfig);

            var specDependencies = GetSpecDependencies(nuSpecDocument);
            var packageDependencies = GetProjectPackages(packageConfig);
            var defaultNamespace = nuSpecDocument.Root.GetDefaultNamespace();

            var currentSpecDependencies =
                specDependencies.Elements().Where(x => x.Name.LocalName == "dependency").ToList();

            foreach (var packageDependency in packageDependencies)
            {
                var id = packageDependency.Attribute("id").Value;
                var version = packageDependency.Attribute("version").Value;

                var specDependency = currentSpecDependencies.SingleOrDefault(x => x.Attribute("id").Value == id);

                if (specDependency == null)
                {
                    // We need to add the package dependency into the nuspec file
                    specDependencies.Add(
                        new XElement(
                            defaultNamespace + "dependency",
                            new XAttribute("id", id),
                            new XAttribute("version", version)));
                }
                else
                {
                    // We need to update the version of the spec
                    specDependency.SetAttributeValue("version", version);
                }
            }
        }

        /// <summary>
        /// Merges the references.
        /// </summary>
        /// <param name="nuSpecDocument">
        /// The nu spec document.
        /// </param>
        /// <param name="projectPath">
        /// The project path.
        /// </param>
        private void MergeReferences(XDocument nuSpecDocument, string projectPath)
        {
            LogMessage("Merging system references from the project");

            var specReferences = GetSpecReferences(nuSpecDocument);
            var references = GetProjectReferences(projectPath);
            var defaultNamespace = nuSpecDocument.Root.GetDefaultNamespace();

            var currentSpecDependencies =
                specReferences.Elements().Where(x => x.Name.LocalName == "frameworkAssembly").ToList();

            foreach (var reference in references)
            {
                var include = reference.Attribute("Include").Value;

                var specAssembly =
                    currentSpecDependencies.SingleOrDefault(x => x.Attribute("assemblyName").Value == include);

                if (specAssembly == null)
                {
                    // We need to add the project reference into the nuspec file
                    specReferences.Add(
                        new XElement(defaultNamespace + "frameworkAssembly", new XAttribute("assemblyName", include)));
                }
                else
                {
                    // We need to update the assembly reference in the spec
                    specAssembly.SetAttributeValue("assemblyName", include);
                }
            }
        }

        /// <summary>
        ///     Gets or sets the file exclusion pattern.
        /// </summary>
        /// <value>
        ///     The file exclusion pattern.
        /// </value>
        [Required]
        public string FileExclusionPattern
        {
            get;
            set;
        }

        /// <summary>
        ///     Gets or sets a value indicating whether the package should include the build number.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the package should include the build number; otherwise, <c>false</c>.
        /// </value>
        [Required]
        public bool IncludeBuildVersion
        {
            get;
            set;
        }

        /// <summary>
        ///     Gets or sets the nuspec path.
        /// </summary>
        /// <value>
        ///     The nuspec path.
        /// </value>
        [Required]
        public string NuSpecPath
        {
            get;
            set;
        }

        /// <summary>
        ///     Gets or sets the primary output assembly.
        /// </summary>
        /// <value>
        ///     The primary output assembly.
        /// </value>
        [Required]
        public string PrimaryOutputAssembly
        {
            get;
            set;
        }

        /// <summary>
        ///     The projects root directory; set to <code>$(MSBuildProjectDirectory)</code> by default.
        /// </summary>
        [Required]
        public string ProjectDirectory
        {
            get;
            set;
        }

        /// <summary>
        ///     The name of the project; by default will be set to $(MSBuildProjectName).
        /// </summary>
        [Required]
        public string ProjectFile
        {
            get;
            set;
        }

        /// <summary>
        ///     Gets or sets the target framework profile.
        /// </summary>
        /// <value>
        ///     The target framework profile.
        /// </value>
        public string TargetFrameworkProfile
        {
            get;
            set;
        }

        /// <summary>
        ///     Gets or sets the target framework version.
        /// </summary>
        /// <value>
        ///     The target framework version.
        /// </value>
        [Required]
        public string TargetFrameworkVersion
        {
            get;
            set;
        }

        /// <summary>
        ///     Gets or sets a value indicating whether the assembly build version should be used as the semver patch version.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the assembly build version should be used as the semver patch version; otherwise, <c>false</c>.
        /// </value>
        [Required]
        public bool UseBuildVersionAsPatch
        {
            get;
            set;
        }
        /// <summary>
        /// Gets or sets a value that will be the Nuget package version. Must be set by the time Merge is called if OverrideNuGetVersion is set
        /// </summary>
        public string NuGetVersion { get; set; }
        /// <summary>
        /// Indicates if we intend to set NuGetVersion. If not used set will continue to try to get the version from the assembly.
        /// Useful if you want the package to be a different version from the assembly (e.g. if using GitVersion etc)
        /// </summary>
        public bool OverrideNuGetVersion { get; set; }

    }
}