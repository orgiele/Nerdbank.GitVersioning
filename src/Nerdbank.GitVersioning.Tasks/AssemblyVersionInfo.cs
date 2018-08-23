﻿namespace Nerdbank.GitVersioning.Tasks
{
    using System;
    using System.CodeDom;
    using System.CodeDom.Compiler;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using Microsoft.Build.Framework;
    using Microsoft.Build.Utilities;

    public class AssemblyVersionInfo : Task
    {
#if NET461
        private static readonly CodeGeneratorOptions codeGeneratorOptions = new CodeGeneratorOptions
        {
            BlankLinesBetweenMembers = false,
            IndentString = "    ",
        };

        private CodeCompileUnit generatedFile;
#else
        private const string FileHeaderComment = @"------------------------------------------------------------------------------
 <auto-generated>
     This code was generated by a tool.
     Runtime Version:4.0.30319.42000

     Changes to this file may cause incorrect behavior and will be lost if
     the code is regenerated.
 </auto-generated>
------------------------------------------------------------------------------
";

        private CodeGenerator generator;

#endif

        [Required]
        public string CodeLanguage { get; set; }

        [Required]
        public string OutputFile { get; set; }

        public bool EmitNonVersionCustomAttributes { get; set; }

        public string AssemblyName { get; set; }

        public string AssemblyVersion { get; set; }

        public string AssemblyFileVersion { get; set; }

        public string AssemblyInformationalVersion { get; set; }

        public string RootNamespace { get; set; }

        public string AssemblyOriginatorKeyFile { get; set; }

        public string AssemblyKeyContainerName { get; set; }

        public string AssemblyTitle { get; set; }

        public string AssemblyProduct { get; set; }

        public string AssemblyCopyright { get; set; }

        public string AssemblyCompany { get; set; }

        public string AssemblyConfiguration { get; set; }

#if NET461
        public override bool Execute()
        {
            if (CodeDomProvider.IsDefinedLanguage(this.CodeLanguage))
            {
                using (var codeDomProvider = CodeDomProvider.CreateProvider(this.CodeLanguage))
                {
                    this.generatedFile = new CodeCompileUnit();
                    this.generatedFile.AssemblyCustomAttributes.AddRange(this.CreateAssemblyAttributes().ToArray());

                    var ns = new CodeNamespace();
                    this.generatedFile.Namespaces.Add(ns);
                    ns.Types.Add(this.CreateThisAssemblyClass());

                    Directory.CreateDirectory(Path.GetDirectoryName(this.OutputFile));
                    FileStream file = null;
                    Utilities.FileOperationWithRetry(() => file = File.OpenWrite(this.OutputFile));
                    using (file)
                    {
                        using (var fileWriter = new StreamWriter(file, new UTF8Encoding(true), 4096, leaveOpen: true))
                        {
                            codeDomProvider.GenerateCodeFromCompileUnit(this.generatedFile, fileWriter, codeGeneratorOptions);
                        }

                        // truncate to new size.
                        file.SetLength(file.Position);
                    }
                }
            }
            else
            {
                this.Log.LogError("CodeDomProvider not available for language: {0}. No version info will be embedded into assembly.", this.CodeLanguage);
            }

            return !this.Log.HasLoggedErrors;
        }

        private CodeTypeDeclaration CreateThisAssemblyClass()
        {
            var thisAssembly = new CodeTypeDeclaration("ThisAssembly")
            {
                IsClass = true,
                IsPartial = true,
                TypeAttributes = TypeAttributes.NotPublic | TypeAttributes.Sealed,
            };

            // CodeDOM doesn't support static classes, so hide the constructor instead.
            thisAssembly.Members.Add(new CodeConstructor { Attributes = MemberAttributes.Private });

            // Determine information about the public key used in the assembly name.
            string publicKey, publicKeyToken;
            bool hasKeyInfo = this.TryReadKeyInfo(out publicKey, out publicKeyToken);

            // Define the constants.
            thisAssembly.Members.AddRange(CreateFields(new Dictionary<string, string>
                {
                    { "AssemblyVersion", this.AssemblyVersion },
                    { "AssemblyFileVersion", this.AssemblyFileVersion },
                    { "AssemblyInformationalVersion", this.AssemblyInformationalVersion },
                    { "AssemblyName", this.AssemblyName },
                    { "AssemblyTitle", this.AssemblyTitle },
                    { "AssemblyProduct", this.AssemblyProduct },
                    { "AssemblyCopyright", this.AssemblyCopyright },
                    { "AssemblyCompany", this.AssemblyCompany },
                    { "AssemblyConfiguration", this.AssemblyConfiguration }
                }).ToArray());
            if (hasKeyInfo)
            {
                thisAssembly.Members.AddRange(CreateFields(new Dictionary<string, string>
                {
                    { "PublicKey", publicKey },
                    { "PublicKeyToken", publicKeyToken },
                }).ToArray());
            }

            // These properties should be defined even if they are empty.
            thisAssembly.Members.Add(CreateField("RootNamespace", this.RootNamespace));

            return thisAssembly;
        }

        private IEnumerable<CodeAttributeDeclaration> CreateAssemblyAttributes()
        {
            yield return DeclareAttribute(typeof(AssemblyVersionAttribute), this.AssemblyVersion);
            yield return DeclareAttribute(typeof(AssemblyFileVersionAttribute), this.AssemblyFileVersion);
            yield return DeclareAttribute(typeof(AssemblyInformationalVersionAttribute), this.AssemblyInformationalVersion);
            if (this.EmitNonVersionCustomAttributes)
            {
                if (!string.IsNullOrEmpty(this.AssemblyTitle))
                    yield return DeclareAttribute(typeof(AssemblyTitleAttribute), this.AssemblyTitle);
                if (!string.IsNullOrEmpty(this.AssemblyProduct))
                    yield return DeclareAttribute(typeof(AssemblyProductAttribute), this.AssemblyProduct);
                if (!string.IsNullOrEmpty(this.AssemblyCompany))
                    yield return DeclareAttribute(typeof(AssemblyCompanyAttribute), this.AssemblyCompany);
                if (!string.IsNullOrEmpty(this.AssemblyCopyright))
                    yield return DeclareAttribute(typeof(AssemblyCopyrightAttribute), this.AssemblyCopyright);
            }
        }

        private static IEnumerable<CodeMemberField> CreateFields(IReadOnlyDictionary<string, string> namesAndValues)
        {
            foreach (var item in namesAndValues)
            {
                if (!string.IsNullOrEmpty(item.Value))
                {
                    yield return CreateField(item.Key, item.Value);
                }
            }
        }

        private static CodeMemberField CreateField(string name, string value)
        {
            return new CodeMemberField(typeof(string), name)
            {
                Attributes = MemberAttributes.Const | MemberAttributes.Assembly,
                InitExpression = new CodePrimitiveExpression(value),
            };
        }

        private static CodeAttributeDeclaration DeclareAttribute(Type attributeType, params CodeAttributeArgument[] arguments)
        {
            var assemblyTypeReference = new CodeTypeReference(attributeType);
            return new CodeAttributeDeclaration(assemblyTypeReference, arguments);
        }

        private static CodeAttributeDeclaration DeclareAttribute(Type attributeType, params string[] arguments)
        {
            return DeclareAttribute(
                attributeType,
                arguments.Select(a => new CodeAttributeArgument(new CodePrimitiveExpression(a))).ToArray());
        }

#else

        public override bool Execute()
        {
            this.generator = this.CreateGenerator();
            if (this.generator != null)
            {
                this.generator.AddComment(FileHeaderComment);
                this.generator.AddBlankLine();
                this.CreateAssemblyAttributes();
                this.CreateThisAssemblyClass();

                Directory.CreateDirectory(Path.GetDirectoryName(this.OutputFile));
                string fileContent = this.generator.GetCode();
                Utilities.FileOperationWithRetry(() => File.WriteAllText(this.OutputFile, fileContent));
            }

            return !this.Log.HasLoggedErrors;
        }

        private void CreateAssemblyAttributes()
        {
            this.generator.DeclareAttribute(typeof(AssemblyVersionAttribute), this.AssemblyVersion);
            this.generator.DeclareAttribute(typeof(AssemblyFileVersionAttribute), this.AssemblyFileVersion);
            this.generator.DeclareAttribute(typeof(AssemblyInformationalVersionAttribute), this.AssemblyInformationalVersion);
            if (this.EmitNonVersionCustomAttributes)
            {
                if (!string.IsNullOrEmpty(this.AssemblyTitle))
                    this.generator.DeclareAttribute(typeof(AssemblyTitleAttribute), this.AssemblyTitle);
                if (!string.IsNullOrEmpty(this.AssemblyProduct))
                    this.generator.DeclareAttribute(typeof(AssemblyProductAttribute), this.AssemblyProduct);
                if (!string.IsNullOrEmpty(this.AssemblyCompany))
                    this.generator.DeclareAttribute(typeof(AssemblyCompanyAttribute), this.AssemblyCompany);
                if (!string.IsNullOrEmpty(this.AssemblyCopyright))
                    this.generator.DeclareAttribute(typeof(AssemblyCopyrightAttribute), this.AssemblyCopyright);
            }
        }

        private void CreateThisAssemblyClass()
        {
            this.generator.StartThisAssemblyClass();

            // Determine information about the public key used in the assembly name.
            string publicKey, publicKeyToken;
            bool hasKeyInfo = this.TryReadKeyInfo(out publicKey, out publicKeyToken);

            // Define the constants.
            var fields = new Dictionary<string, string>
                {
                    { "AssemblyVersion", this.AssemblyVersion },
                    { "AssemblyFileVersion", this.AssemblyFileVersion },
                    { "AssemblyInformationalVersion", this.AssemblyInformationalVersion },
                    { "AssemblyName", this.AssemblyName },
                    { "AssemblyTitle", this.AssemblyTitle },
                    { "AssemblyProduct", this.AssemblyProduct },
                    { "AssemblyCopyright", this.AssemblyCopyright },
                    { "AssemblyCompany", this.AssemblyCompany },
                    { "AssemblyConfiguration", this.AssemblyConfiguration }
                };
            if (hasKeyInfo)
            {
                fields.Add("PublicKey", publicKey);
                fields.Add("PublicKeyToken", publicKeyToken);
            }

            foreach (var pair in fields)
            {
                if (!string.IsNullOrEmpty(pair.Value))
                {
                    this.generator.AddThisAssemblyMember(pair.Key, pair.Value);
                }
            }

            // These properties should be defined even if they are empty.
            this.generator.AddThisAssemblyMember("RootNamespace", this.RootNamespace);

            this.generator.EndThisAssemblyClass();
        }
        private CodeGenerator CreateGenerator()
        {
            switch (this.CodeLanguage.ToLowerInvariant())
            {
                case "c#":
                    return new CSharpCodeGenerator();
                case "visual basic":
                case "visualbasic":
                case "vb":
                    return new VisualBasicCodeGenerator();
                default:
                    this.Log.LogError("Code provider not available for language: {0}. No version info will be embedded into assembly.", this.CodeLanguage);
                    return null;
            }
        }

        private abstract class CodeGenerator
        {
            protected readonly StringBuilder codeBuilder;

            internal CodeGenerator()
            {
                this.codeBuilder = new StringBuilder();
            }

            internal abstract void AddComment(string comment);

            internal abstract void DeclareAttribute(Type type, string arg);

            internal abstract void StartThisAssemblyClass();

            internal abstract void AddThisAssemblyMember(string name, string value);

            internal abstract void EndThisAssemblyClass();

            internal string GetCode() => this.codeBuilder.ToString();

            internal void AddBlankLine()
            {
                this.codeBuilder.AppendLine();
            }

            protected void AddCodeComment(string comment, string token)
            {
                var sr = new StringReader(comment);
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    this.codeBuilder.Append(token);
                    this.codeBuilder.AppendLine(line);
                }
            }
        }

        private class CSharpCodeGenerator : CodeGenerator
        {
            internal override void AddComment(string comment)
            {
                this.AddCodeComment(comment, "//");
            }

            internal override void DeclareAttribute(Type type, string arg)
            {
                this.codeBuilder.AppendLine($"[assembly: {type.FullName}(\"{arg}\")]");
            }

            internal override void StartThisAssemblyClass()
            {
                this.codeBuilder.AppendLine("internal static partial class ThisAssembly {");
            }

            internal override void AddThisAssemblyMember(string name, string value)
            {
                this.codeBuilder.AppendLine($"    internal const string {name} = \"{value}\";");
            }

            internal override void EndThisAssemblyClass()
            {
                this.codeBuilder.AppendLine("}");
            }
        }

        private class VisualBasicCodeGenerator : CodeGenerator
        {
            internal override void AddComment(string comment)
            {
                this.AddCodeComment(comment, "'");
            }

            internal override void DeclareAttribute(Type type, string arg)
            {
                this.codeBuilder.AppendLine($"<Assembly: {type.FullName}(\"{arg}\")>");
            }

            internal override void StartThisAssemblyClass()
            {
                this.codeBuilder.AppendLine("Partial Friend NotInheritable Class ThisAssembly");
            }

            internal override void AddThisAssemblyMember(string name, string value)
            {
                this.codeBuilder.AppendLine($"    Friend Const {name} As String = \"{value}\"");
            }

            internal override void EndThisAssemblyClass()
            {
                this.codeBuilder.AppendLine("End Class");
            }
        }
#endif

        private static string ToHex(byte[] data)
        {
            return BitConverter.ToString(data).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// Gets the public key from a key container.
        /// </summary>
        /// <param name="containerName">The name of the container.</param>
        /// <returns>The public key.</returns>
        private static byte[] GetPublicKeyFromKeyContainer(string containerName)
        {
            throw new NotImplementedException();
        }

        private static byte[] GetPublicKeyFromKeyPair(byte[] keyPair)
        {
            byte[] publicKey;
            if (CryptoBlobParser.TryGetPublicKeyFromPrivateKeyBlob(keyPair, out publicKey))
            {
                return publicKey;
            }
            else
            {
                throw new ArgumentException("Invalid keypair");
            }
        }

        private bool TryReadKeyInfo(out string publicKey, out string publicKeyToken)
        {
            try
            {
                byte[] publicKeyBytes = null;
                if (!string.IsNullOrEmpty(this.AssemblyOriginatorKeyFile) && File.Exists(this.AssemblyOriginatorKeyFile))
                {
                    if (Path.GetExtension(this.AssemblyOriginatorKeyFile).Equals(".snk", StringComparison.OrdinalIgnoreCase))
                    {
                        byte[] keyBytes = File.ReadAllBytes(this.AssemblyOriginatorKeyFile);
                        bool publicKeyOnly = keyBytes[0] != 0x07;
                        publicKeyBytes = publicKeyOnly ? keyBytes : GetPublicKeyFromKeyPair(keyBytes);
                    }
                }
                else if (!string.IsNullOrEmpty(this.AssemblyKeyContainerName))
                {
                    publicKeyBytes = GetPublicKeyFromKeyContainer(this.AssemblyKeyContainerName);
                }

                if (publicKeyBytes != null && publicKeyBytes.Length > 0) // If .NET 2.0 isn't installed, we get byte[0] back.
                {
                    publicKey = ToHex(publicKeyBytes);
                    publicKeyToken = ToHex(CryptoBlobParser.GetStrongNameTokenFromPublicKey(publicKeyBytes));
                }
                else
                {
                    if (publicKeyBytes != null)
                    {
                        this.Log.LogWarning("Unable to emit public key fields in ThisAssembly class because .NET 2.0 isn't installed.");
                    }

                    publicKey = null;
                    publicKeyToken = null;
                    return false;
                }

                return true;
            }
            catch (NotImplementedException)
            {
                publicKey = null;
                publicKeyToken = null;
                return false;
            }
        }
    }
}
