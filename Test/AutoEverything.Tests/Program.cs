using System;
using System.IO;
using System.Reflection;

namespace AutoEverything.Tests
{
    /// <summary>
    /// 测试入口：零依赖控制台运行器，不使用 xunit/NUnit 等测试框架。
    /// 返回非零退出码表示有测试失败，便于 CI 与 Makefile 集成。
    ///
    /// 程序集加载策略：
    /// 主项目 AutoEverything.dll 依赖 RimWorld 的 Assembly-CSharp.dll / UnityEngine.*.dll，
    /// 这些 DLL 不复制到测试输出目录（Private=false）。运行时用 AssemblyResolve 钩子
    /// 从 RimWorld 安装目录与主项目输出目录加载。路径与主项目 .csproj 的 RimWorldPath 一致。
    /// </summary>
    public static class Program
    {
        // 与 Source/AutoEverything/AutoEverything.csproj 的 RimWorldPath / HarmonyPath 保持一致
        // 主项目 AutoEverything.dll 由 ProjectReference Private=true 复制到同目录；
        // RimWorld / Harmony DLL 用 AssemblyResolve 从游戏目录加载
        private static readonly string[] SearchDirs =
        {
            AppDomain.CurrentDomain.BaseDirectory,
            @"e:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed",
            @"e:\SteamLibrary\steamapps\workshop\content\294100\2009463077\Current\Assemblies"
        };

        public static int Main()
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            try
            {
                Console.WriteLine("=== AutoEverything.Tests ===");
                int failures = 0;
                failures += ApplySkillFloorCoreTests.RunAll();
                failures += EvaluateAutoTierCoreTests.RunAll();
                failures += PawnMarkerTests.RunAll();
                failures += GearAllocatorTests.RunAll();

                if (failures == 0)
                {
                    Console.WriteLine("All tests passed.");
                    return 0;
                }
                Console.WriteLine($"{failures} test(s) failed.");
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UNHANDLED EXCEPTION: {ex.GetType().FullName}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return 2;
            }
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name + ".dll";
            for (int i = 0; i < SearchDirs.Length; i++)
            {
                string path = Path.Combine(SearchDirs[i], name);
                if (File.Exists(path))
                {
                    try
                    {
                        return Assembly.LoadFrom(path);
                    }
                    catch
                    {
                        // 加载失败继续尝试下一个目录
                    }
                }
            }
            return null;
        }
    }
}
