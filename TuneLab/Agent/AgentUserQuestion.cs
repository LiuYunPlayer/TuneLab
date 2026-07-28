using System.Collections.Generic;

namespace TuneLab.Agent;

// agent 向用户提出的一个问题（宿主据此渲染内联卡片）。
// Options 为空 = 纯开放提问（卡片只有自由文本框）；Multiple 决定选项是多选还是单选。
internal readonly record struct AgentUserQuestion(string Question, IReadOnlyList<string> Options, bool Multiple);

// 用户的回答。两部分【各自独立】：可以只选选项、只写文本，或两者都有——
// 所以不能把它压成单个字符串，否则模型无从区分"选了 A"与"写了 A 这几个字"。
internal readonly record struct AgentUserAnswer(IReadOnlyList<string> SelectedOptions, string? Text);
