using System.Runtime.CompilerServices;

// 暴露 internal 成员给测试项目，便于纯逻辑单元测试。
// .NET SDK 风格项目在 net472 + GenerateAssemblyInfo=false 下，
// .csproj 中的 <InternalsVisibleTo> item 不会自动生成 assembly 属性，
// 必须手动用 [assembly: InternalsVisibleTo] 声明。
[assembly: InternalsVisibleTo("AutoEverything.Tests")]
