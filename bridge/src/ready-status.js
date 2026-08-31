export function buildReadyStatus({ pid, port, startedAt }) {
  return {
    pid,
    port,
    modelMode: 'follow-local-codex',
    startedAt,
  };
}
