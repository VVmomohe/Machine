commit bf25c27028a7e08057d57a7ecfef45be919980c4
date: 2026-07-20 14:50:41 +0800
author: fl116 <fl116@local>

init: Machine37 工程初始版本（含 ankh.com 串口底层，排除 Library/Temp 等生成目录）
========================================

commit 0c05371219041f3095f060925d7baecd69a5c50c
date: 2026-07-20 15:36:16 +0800
author: fl116 <fl116@local>

提交
========================================

commit a281fcdf7003b5e275bab8b8edf28a38ca6c133f
date: 2026-07-23 12:01:02 +0800
author: fl116 <fl116@local>

备份
========================================

commit 90a0513e5c4ecb966dbddbdb7160055134fc9f13
date: 2026-07-23 15:20:37 +0800
author: fl116 <fl116@local>

三七机 Hold&Spin / 免费小游戏 逻辑与演出基线（7/20-7/23）

结算流程重写：基础旋转只 EnterHoldSpin 展示初始状态，每轮由 Start 键触发
AdvanceHoldSpin 单轮，IsOver 才统一结算并进入 Mini。

免费次数膨胀修复：区分 Scatter 原始奖励与 FREE 火球增量，避免重复进 Mini。

Mini 火球：独立火球生成（fbProb 1.5%）、持久 overlay 固定、跨会话残留清理
（RestoreMainBoard 清 overlay）；选项 A 消除重影——卷轴内不再渲染火球符号，
仅由 ShowFeatureState 重建的持久 overlay 显示。

m_effect 预警特效：近满列/释放即关，文件释放回滚那一刻关（ReleaseReel）。

音效：13 创建火球 / 1 列停稳 / 23 满列收集完成 / 111 普通 ICON 赢分 /
18 满列且列倍率>8 / 110 满列且列倍率≤8 / 7 进 Mini / 11 主 BGM / 8 Mini BGM。

清理：移除 testForceRowFireballs；提取 CountFreeFireballs / ShowJackpotEffectsForReel
helper；精简调试日志；ReelView.HoldSpin 拆分为 4 个 partial。

gitignore：忽略 fmod_editor.log。
========================================

commit 8ddc059f6a4fdce27fb7acbdffcfccf86353c1fe
date: 2026-07-23 15:21:17 +0800
author: fl116 <fl116@local>

chore: 取消追踪 fmod_editor.log（已加入 .gitignore）
========================================

