/// <reference lib="webworker" />
addEventListener('message', ({ data }) => {
  try {
    const expression = new RegExp(data.pattern);
    const values: string[] = Array.isArray(data.raw) ? data.raw : [data.raw];
    const selected: string[] = [];
    let groups: Record<string, string> = {};
    for (const value of values) {
      const match = expression.exec(value);
      if (!match) continue;
      groups = { ...(match.groups ?? {}) };
      match.slice(1).forEach((item, index) => { if (item !== undefined) groups[String(index + 1)] = item; });
      const result = data.group ? (/^\d+$/.test(data.group) ? match[Number(data.group)] : match.groups?.[data.group]) : match[0];
      if (result === undefined) throw new Error(`Regex group '${data.group}' bulunamadı.`);
      selected.push(result);
    }
    postMessage({ selected, groups });
  } catch (error) { postMessage({ error: error instanceof Error ? error.message : String(error) }); }
});
