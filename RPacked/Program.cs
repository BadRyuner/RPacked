using System.Runtime.CompilerServices;
using System.Text;
using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Cloning;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.File;
using System.IO.Compression;

namespace RPacked;

internal class Program
{
	static TypeDefinition ThisClass = ModuleDefinition.FromModule(typeof(Program).Assembly.ManifestModule)
		.TopLevelTypes
		.First(t => t.Name == "Program");

	static void Main(string[] args)
	{
#if DEBUG
		args = new[] { "RPacked.dll" };
#else
		if (args.Length == 0)
		{
			Console.WriteLine("Usage: RPacked.exe path/to/your/app.dll");
			return;
		}
#endif
		DoWork(args[0]);
		Console.WriteLine("Done!");
		//Console.ReadLine();
	}

	static void DoWork(string assembly)
	{
		using var payload = new MemoryStream(1024*8);

		var assemblyInfo = new FileInfo(assembly);
		var assemblyName = assemblyInfo.Name;
		var newExeName = assemblyName.Replace(".dll", ".exe");

		var outputDir = assemblyInfo.Directory.CreateSubdirectory("Output");
		var cfgName = assemblyInfo.FullName.Replace(".dll", ".runtimeconfig.json");

		File.Copy(cfgName, Path.Combine(outputDir.FullName, "bootstrapper.runtimeconfig.json"), true);

		Console.WriteLine("Created runtimeconfig");

		var file = PEFile.FromFile("bootstrapper.exe");
		var kek = file.Sections.First(s => s.Name == ".kek");

		var openedMainDll = ModuleDefinition.FromFile(assembly);
		var entry = openedMainDll.ManagedEntryPointMethod;
		InjectNativeEntry(payload, entry);

		Console.WriteLine("Injected custom entry point");

		using var ms = new MemoryStream(1024*4);
		openedMainDll.Write(ms);
		var mainDll = ms.ToArray();

		payload.Write(BitConverter.GetBytes(mainDll.Length));
		payload.Write(mainDll);

		Console.WriteLine("Added exe dll");

		var offset = payload.Position;
		var dllsCount = 0;
		payload.Write(new byte[] { 0,0,0,0 });

		foreach(var dll in assemblyInfo.Directory.EnumerateFiles("*.dll"))
		{
			if (dll.Name == assemblyName)
				continue;

			try
			{
				var isNet = System.Reflection.AssemblyName.GetAssemblyName(dll.FullName);
				if (isNet != null)
				{
					dllsCount++;
					var bytes = File.ReadAllBytes(dll.FullName);
					var align = (bytes.Length + 4) % 4;

					if (align > 0)
						Console.WriteLine("Align reference dll...");

					payload.Write(BitConverter.GetBytes(bytes.Length + align));
					payload.Write(bytes);

					while (align > 0)
						payload.Write(new byte[align]);

					Console.WriteLine($"Added {isNet.FullName}");
				}
			}
			catch {}
		}

		Console.WriteLine("Fixed dlls count");

		var oldPos = payload.Position;
		payload.Position = offset;
		payload.Write(BitConverter.GetBytes(dllsCount));
		payload.Position = oldPos;

		Console.WriteLine($"Writed {payload.Position} pure bytes");

		Console.WriteLine("Using Brotli...");

		using var output = new MemoryStream();
		using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize)) // you can change level to Optimal
		{
			payload.Position = 0;
			payload.CopyTo(brotli);
		}

		Console.WriteLine("Writing data..."); // 945744 - 359273
		var newSegment = new SegmentBuilder();
		var result = output.ToArray();
		newSegment.Add(new DataSegment(BitConverter.GetBytes(result.Length)));
		newSegment.Add(new DataSegment(result));
		kek.Contents = newSegment;

		Console.WriteLine($"Writed {result.Length} compressed bytes");

		file.Write(Path.Combine(outputDir.FullName, newExeName));
	}

	internal static void InjectNativeEntry(MemoryStream builder, MethodDefinition entry)
	{
		var decl = entry.DeclaringType;
		var typeName = decl.Namespace?.Length > 0 ? $"{decl.Namespace}.{decl.Name}" : decl.Name.ToString();

		var newEntry = (MethodDefinition)new MemberCloner(entry.DeclaringType.Module)
			.Include(ThisClass.Methods.First(m => m.Name == nameof(NativeMain)))
			.Clone()
			.ClonedMembers.First();


		var newEntryInstructions = newEntry.CilMethodBody.Instructions;
		newEntryInstructions.RemoveAt(newEntryInstructions.Count-1); // remove last ret
		if (entry.Signature.ReturnsValue)
			newEntryInstructions.RemoveAt(newEntryInstructions.Count - 1); // remove ldc.i4 0
		if (entry.Parameters.Count > 0)
		{
			var newarr = newEntryInstructions.First(instr => instr.OpCode.Code == CilCode.Newarr);
			var index = newEntryInstructions.IndexOf(newarr);
			var stloc = newEntryInstructions[index+1];
			var local = stloc.GetLocalVariable(newEntryInstructions.Owner.LocalVariables);
			newEntryInstructions.Add(CilOpCodes.Ldloc, local);
		}
		newEntryInstructions.Add(CilOpCodes.Call, entry);
		newEntryInstructions.Add(CilOpCodes.Ret);

		entry.DeclaringType.Methods.Add(newEntry);
		var entryEntryStr = $"{typeName}, {decl.Module.Name.ToString().Replace(".dll", null)}\0";
		var entryEntry = Encoding.Unicode.GetBytes(entryEntryStr);

		using var ms = new MemoryStream(512);
		var writer = new BinaryWriter(ms);

		var align = (entryEntry.Length + 4) % 4;
		if (align > 0) 
			Console.WriteLine("Align main dll...");
		writer.Write(entryEntry.Length + align);
		writer.Write(entryEntry);
		while (align > 0)
		{
			writer.Write((byte)0);
			align--;
		}
		var arr = ms.ToArray();
		builder.Write(arr);
	}

	internal static unsafe int NativeMain(IntPtr args, int sizeBytes)
	{
		var argCount = sizeBytes;
		var arguments = (char**)args;
		var sharpargs = new string[argCount];
		for(int i = 0; i < argCount; i++)
		{
			sharpargs[i] = new string(arguments[i]);
		}

#if DEBUG
        foreach (var item in sharpargs)
        {
            Console.WriteLine($"DECODED: {item}");
        }
#endif

        return 0;
	}
}
