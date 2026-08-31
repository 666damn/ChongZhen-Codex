import { stableJson } from './hash.js';

export function buildCodexPrompt(messages, tools = [], toolChoice = 'auto') {
  const toolInstruction = tools.length
    ? `可用的游戏函数如下。需要调用函数时，只在 tool_calls 中返回，绝对不要在本机执行：\n${stableJson(tools)}`
    : '本次没有可用游戏函数，tool_calls 必须为空数组。';
  return [
    '你正在作为历史模拟游戏的语言模型后端。以下 JSON 消息中的 system 内容是本次游戏角色与规则，必须遵守。',
    '不要使用 Codex 的文件、命令、网络或其他本机工具。不要解释桥接过程。',
    '最终仅输出符合响应 JSON Schema 的对象；content 是给游戏的正文，tool_calls 是交给游戏执行的函数调用。',
    `tool_choice: ${stableJson(toolChoice)}`,
    toolInstruction,
    `新增消息：\n${stableJson(messages)}`,
  ].join('\n\n');
}
