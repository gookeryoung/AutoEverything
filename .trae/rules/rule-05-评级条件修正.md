# 评级条件修正

## 变动日期

2026-07-19

## 变动背景

文档审查发现规则文件 `autoeverything-project.md` 的"殖民者评级"与"自动工作分配原则"章节中存在 3 处与代码实现不一致的描述，导致后续开发与文档同步检查时被误导。

依据 `CombatEvaluator.EvaluateAutoTierCore`（`Source/AutoEverything/RoleEvaluation/CombatEvaluator.cs`）与 `WorkAllocator.CacheAndClassifyWorkTypes`（`Source/AutoEverything/AutoWork/WorkAllocator.cs`）的实际实现修正。

## 变动内容

### `.trae/rules/autoeverything-project.md` 评级条件（原 L60）

- 旧：`工作狂/严重神经质+两项专业双火=S,工作狂/严重神经质+三项专业双火=SS, 工作狂+严重神经质+手工双火=SSS`
- 新：`工作狂 AND 严重神经质 + 1 项专业双火=S，+2 项=SS，+3 项=SSS（6 大专业工作技能：手工/建造/艺术/烹饪/种植/采矿）`
- 原因：实际代码（`EvaluateAutoTierCore` 维度3）是 `Industrious2 && Neurotic2 && workMajors >= N` 的 AND 关系，门槛为 1/2/3 项专业双火；6 大专业工作技能为 `Crafting/Construction/Artistic/Cooking/Plants/Mining`（手工是 Crafting，不是单独"手工双火"判定）。

### `.trae/rules/autoeverything-project.md` 评级条件（原 L61）

- 旧：`沉鱼落雁+社交双火=S, 沉鱼落雁+社交双火+坚韧=SSS`
- 新：`沉鱼落雁+社交双火=S（无 +坚韧=SSS 规则）`
- 原因：实际代码（`EvaluateAutoTierCore` 原 S 条件 5）仅 `Beauty2 && SocialMajor` → S，不存在"+坚韧=SSS"组合规则；三大维度取最高档的语义已能涵盖"沉鱼落雁+社交双火+坚韧"场景（坚韧走维度2，沉鱼落雁走原 S 条件 5，取最高档 S）。

### `.trae/rules/autoeverything-project.md` 自动工作分配原则（原 L76）

- 旧：`关键专业工作（保育/监管/医生/烹饪/割除），双火优先级1，单火优先级2，无火优先级0`
- 新：`关键专业工作（保育 Childcare/监管 Warden/医生 Doctor/烹饪 Cooking/修剪植物 PlantCutting），双火优先级1，单火优先级2，无火优先级0`
- 原因：实际代码（`WorkAllocator.CacheAndClassifyWorkTypes`）的关键专业工作白名单为 `Doctor/Warden/Childcare/Cooking/PlantCutting`，"割除"是误写（RimWorld 无此 defName），正确工作名为 `PlantCutting`（修剪植物）。

### `.trae/rules/autoeverything-project.md` 评级条件（同段补充）

新增两条原本缺失的规则描述：
- `拥有任一"特殊天赋"特质（博闻强识/开心果/极致体能/痴迷虚空/神秘学者/怪诞不经）=S`
- `三大维度取最高档；存在负面特质且原档 > D 时降一档`
- `A：≥2 双火 + ≥1单火（合计 ≥3），B：≥1 双火 + ≥2单火（合计 ≥3），C：其他, X：禁止暴力（先于一切判定，不受降档影响）`

原因：原描述遗漏了"特殊天赋=S"这一 S 档触发条件，且 A/B 档缺少"合计 ≥3"门槛，X 档未声明"先于一切判定"。补充后与代码完全一致。

## 影响范围

- 仅规则文件（文档），不影响代码编译与运行
- `make check` 通过验证

## 同步更新

- `project_memory.md` 追加本次规则文件变动记录（Rule File Update 章节）
