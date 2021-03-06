﻿using Ionic.Zip;
using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Core;
using log4net.Layout;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Terraria.Localization;

namespace Terraria.ModLoader
{
	public static class Logging
	{
		public static readonly string LogDir = Path.Combine(Main.SavePath, "Logs");
		public static string LogPath { get; private set; }

		internal static ILog Terraria { get; } = LogManager.GetLogger("Terraria");
		internal static ILog tML { get; } = LogManager.GetLogger("tML");

#if CLIENT
		internal const string side = "client";
#else
		internal const string side = "server";
#endif

		internal static void Init() {
			if (Program.LaunchParameters.ContainsKey("-build"))
				return;

#if !WINDOWS
			new Hook(typeof(Encoding).GetMethod(nameof(Encoding.GetEncoding), new[] { typeof(string) }), new hook_GetEncoding(HookGetEncoding));
#endif

			if (!Directory.Exists(LogDir))
				Directory.CreateDirectory(LogDir);

			ConfigureAppenders();

			tML.InfoFormat("Starting {0}{1} {2}", ModLoader.versionedName, ModLoader.compressedPlatformRepresentation, side);
			tML.InfoFormat("Running on {0} {1}", FrameworkVersion.Framework, FrameworkVersion.Version);
			tML.InfoFormat("Executable: {0}", Assembly.GetEntryAssembly().Location);
			tML.InfoFormat("Working Directory: {0}", Path.GetFullPath(Directory.GetCurrentDirectory()));
			tML.InfoFormat("Launch Parameters: {0}", string.Join(" ", Program.LaunchParameters.Select(p => (p.Key + " " + p.Value).Trim())));

			HookModuleLoad();
			AppDomain.CurrentDomain.UnhandledException += (s, args) => tML.Error("Unhandled Exception", args.ExceptionObject as Exception);
			PrettifyStackTraceSources();

			if (ModCompile.DeveloperMode) {
				tML.Info("Developer mode enabled");
				LogFirstChanceExceptions(true);
			}
		}

		private static void ConfigureAppenders() {
			var layout = new PatternLayout {
				ConversionPattern = "[%d{HH:mm:ss}] [%t/%level] [%logger]: %m%n"
			};
			layout.ActivateOptions();

			var appenders = new List<IAppender>();
#if CLIENT
			appenders.Add(new ConsoleAppender {
				Name = "ConsoleAppender",
				Layout = layout
			});
#else
			appenders.Add(new DebugAppender {
				Name = "DebugAppender",
				Layout = layout
			});
#endif

			var fileAppender = new FileAppender {
				Name = "FileAppender",
				File = LogPath = Path.Combine(LogDir, RollLogs(side)),
				AppendToFile = false,
				Encoding = Encoding.UTF8,
				Layout = layout
			};
			fileAppender.ActivateOptions();
			appenders.Add(fileAppender);

			BasicConfigurator.Configure(appenders.ToArray());
		}

		private static string RollLogs(string baseName) {
			var pattern = new Regex($"{baseName}(\\d*)\\.log");
			var existingLogs = Directory.GetFiles(LogDir).Where(s => pattern.IsMatch(Path.GetFileName(s))).ToList();

			if (!existingLogs.All(CanOpen)) {
				int n = existingLogs.Select(s => {
					var tok = pattern.Match(Path.GetFileName(s)).Groups[1].Value;
					return tok.Length == 0 ? 1 : int.Parse(tok);
				}).Max();
				return $"{baseName}{n + 1}.log";
			}

			foreach (var existingLog in existingLogs.OrderBy(File.GetCreationTime))
				Archive(existingLog);

			DeleteOldArchives();

			return $"{baseName}.log";
		}

		private static bool CanOpen(string fileName) {
			try {
				using (new FileStream(fileName, FileMode.Append)) ;
				return true;
			}
			catch (IOException) {
				return false;
			}
		}

		private static void Archive(string logPath) {
			var time = File.GetCreationTime(logPath);
			int n = 1;

			var pattern = new Regex($"{time:yyyy-MM-dd}-(\\d+)\\.zip");
			var existingLogs = Directory.GetFiles(LogDir).Where(s => pattern.IsMatch(Path.GetFileName(s))).ToList();
			if (existingLogs.Count > 0)
				n = existingLogs.Select(s => int.Parse(pattern.Match(Path.GetFileName(s)).Groups[1].Value)).Max() + 1;

			using (var zip = new ZipFile(Path.Combine(LogDir, $"{time:yyyy-MM-dd}-{n}.zip"), Encoding.UTF8)) {
				zip.AddFile(logPath, "");
				zip.Save();
			}

			File.Delete(logPath);
		}

		private const int MAX_LOGS = 20;
		private static void DeleteOldArchives() {
			var pattern = new Regex(".*\\.zip");
			var existingLogs = Directory.GetFiles(LogDir).Where(s => pattern.IsMatch(Path.GetFileName(s))).OrderBy(File.GetCreationTime).ToList();
			foreach (var f in existingLogs.Take(existingLogs.Count - MAX_LOGS)) {
				try {
					File.Delete(f);
				}
				catch (IOException) { }
			}
		}

		private static void HookModuleLoad() {
			AssemblyManager.AssemblyResolveEarly((sender, args) => {
				tML.DebugFormat("Assembly Resolve: {0} -> {1}", args.RequestingAssembly, args.Name);
				return null;
			});
		}

		internal static void LogFirstChanceExceptions(bool enabled) {
			if (enabled) {
				if (FrameworkVersion.Framework == "Mono")
					tML.Warn("First-chance exception reporting is not implemented on Mono");

				AppDomain.CurrentDomain.FirstChanceException += FirstChanceExceptionHandler;
			}
			else {
				AppDomain.CurrentDomain.FirstChanceException -= FirstChanceExceptionHandler;
			}
		}

		private static HashSet<string> pastExceptions = new HashSet<string>();
		internal static void ResetPastExceptions() => pastExceptions.Clear();

		private static HashSet<string> ignoreSources = new HashSet<string> {
			"MP3Sharp"
		};
		public static void IgnoreExceptionSource(string source) => ignoreSources.Add(source);

		private static List<string> ignoreContents = new List<string> {
			"Terraria.ModLoader.ModCompile",
			"Delegate.CreateDelegateNoSecurityCheck",
			"MethodBase.GetMethodBody",
			"Terraria.Net.Sockets.TcpSocket.Terraria.Net.Sockets.ISocket.AsyncSend", //client disconnects from server
			"System.Diagnostics.Process.Kill",//attempt to kill non-started process when joining server
		};

		public static void IgnoreExceptionContents(string source) {
			if (!ignoreContents.Contains(source))
				ignoreContents.Add(source);
		}

		private static Exception previousException;
		private static void FirstChanceExceptionHandler(object sender, FirstChanceExceptionEventArgs args) {
			if (args.Exception == previousException ||
				args.Exception is ThreadAbortException ||
				ignoreSources.Contains(args.Exception.Source))
				return;

			var stackTrace = new StackTrace(true);
			PrettifyStackTraceSources(stackTrace.GetFrames());
			var traceString = stackTrace.ToString();

			if (ignoreContents.Any(traceString.Contains))
				return;

			traceString = traceString.Substring(traceString.IndexOf('\n'));
			var exString = args.Exception.GetType() + ": " + args.Exception.Message + traceString;
			if (!pastExceptions.Add(exString))
				return;

			previousException = args.Exception;
			var msg = args.Exception.Message + " " + Language.GetTextValue("tModLoader.RuntimeErrorSeeLogsForFullTrace", Path.GetFileName(LogPath));
#if CLIENT
			if (Main.ContentLoaded) {
				float soundVolume = Main.soundVolume;
				Main.soundVolume = 0f;
				Main.NewText(msg, Microsoft.Xna.Framework.Color.OrangeRed);
				Main.soundVolume = soundVolume;
			}
#else
			Console.ForegroundColor = ConsoleColor.DarkMagenta;
			Console.WriteLine(msg);
			Console.ResetColor();
#endif
			tML.Warn(Language.GetTextValue("tModLoader.RuntimeErrorSilentlyCaughtException") + '\n' + exString);
		}

		private static Regex statusRegex = new Regex(@"(.+?)[: \d]*%$");
		internal static void LogStatusChange(string oldStatusText, string newStatusText) {
			// trim numbers and percentage to reduce log spam
			var oldBase = statusRegex.Match(oldStatusText).Groups[1].Value;
			var newBase = statusRegex.Match(newStatusText).Groups[1].Value;
			if (newBase != oldBase && newBase.Length > 0)
				LogManager.GetLogger("StatusText").Info(newBase);
		}

		internal static void ServerConsoleLine(string msg) => ServerConsoleLine(msg, Level.Info);
		internal static void ServerConsoleLine(string msg, Level level, Exception ex = null, ILog log = null) {
			if (level == Level.Warn)
				Console.ForegroundColor = ConsoleColor.Yellow;
			else if (level == Level.Error)
				Console.ForegroundColor = ConsoleColor.Red;

			Console.WriteLine(msg);
			Console.ResetColor();

			(log ?? Terraria).Logger.Log(null, level, msg, ex);
		}

		private delegate Encoding orig_GetEncoding(string name);
		private delegate Encoding hook_GetEncoding(orig_GetEncoding orig, string name);
		private static Encoding HookGetEncoding(orig_GetEncoding orig, string name) {
			if (name == "IBM437")
				return null;

			return orig(name);
		}

		private static readonly FieldInfo f_strFileName =
			typeof(StackFrame).GetField("strFileName", BindingFlags.Instance | BindingFlags.NonPublic);

		private static readonly Assembly TerrariaAssembly = Assembly.GetExecutingAssembly();

		private delegate void ctor_StackTrace(StackTrace self, Exception e, bool fNeedFileInfo);
		private delegate void hook_StackTrace(ctor_StackTrace orig, StackTrace self, Exception e, bool fNeedFileInfo);
		private static void HookStackTraceEx(ctor_StackTrace orig, StackTrace self, Exception e, bool fNeedFileInfo) {
			orig(self, e, fNeedFileInfo);
			if (fNeedFileInfo)
				PrettifyStackTraceSources(self.GetFrames());
		}

		public static void PrettifyStackTraceSources(StackFrame[] frames) {
			if (frames == null)
				return;

			foreach (var frame in frames) {
				string filename = frame.GetFileName();
				var assembly = frame.GetMethod()?.DeclaringType?.Assembly;
				if (filename == null || assembly == null)
					continue;

				string trim;
				if (AssemblyManager.GetAssemblyOwner(assembly, out var modName))
					trim = modName;
				else if (assembly == TerrariaAssembly)
					trim = "tModLoader";
				else
					continue;

				int idx = filename.LastIndexOf(trim, StringComparison.InvariantCultureIgnoreCase);
				if (idx > 0) {
					filename = filename.Substring(idx);
					f_strFileName.SetValue(frame, filename);
				}
			}
		}

		private static void PrettifyStackTraceSources() {
			if (f_strFileName == null)
				return;

			new Hook(typeof(StackTrace).GetConstructor(new[] { typeof(Exception), typeof(bool) }), new hook_StackTrace(HookStackTraceEx));
		}
	}
}
