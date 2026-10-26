import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

const root = new URL("../../TaskbarLyrics.App/Web/Settings/", import.meta.url);
const settingKey = "runWithQQMusic";

async function createSettingsDom(settings = {}) {
  const names = ["settings.html", "bridge.js", "hotkeys.js", "state.js", "display-presentation.js", "player-compatibility.js", "settings.js"];
  const [html, ...scripts] = await Promise.all(names.map(name => readFile(new URL(name, root), "utf8")));
  const dom = new JSDOM(html, { runScripts: "outside-only" });
  const sent = [];
  dom.window.chrome = { webview: { postMessage: value => sent.push(JSON.parse(value)) } };
  dom.window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
  dom.window.requestAnimationFrame = callback => { callback(0); return 0; };
  dom.window.HTMLMediaElement.prototype.load = () => {};
  dom.window.HTMLMediaElement.prototype.pause = () => {};
  dom.window.HTMLMediaElement.prototype.play = () => Promise.resolve();
  dom.window.HTMLElement.prototype.scrollIntoView = () => {};
  scripts.forEach(script => dom.window.eval(script));

  const hydrate = next => dom.window.settingsApp.receive({
    version: 1,
    type: "settingsState",
    payload: { settings: structuredClone({ ...settings, ...next }), fonts: [] }
  });
  hydrate({});
  dom.window.settingsApp.receive({ version: 1, type: "navigate", payload: { page: "general" } });

  const checkbox = dom.window.document.querySelector(`[data-setting="${settingKey}"]`);
  return { dom, checkbox, sent, hydrate };
}

describe("QQ Music lifecycle setting", () => {
  it("is available on the general page with a native switch and defaults off", async () => {
    const { dom, checkbox } = await createSettingsDom();

    expect(checkbox).not.toBeNull();
    expect(checkbox.checked).toBe(false);
    expect(checkbox.parentElement.querySelector(".switch-track")).not.toBeNull();
    expect(checkbox.getAttribute("aria-label")).toBe("关闭 QQ 音乐后退出");

    dom.window.close();
  });

  it("posts the setting update when toggled", async () => {
    const { dom, checkbox, sent } = await createSettingsDom();

    checkbox.checked = true;
    checkbox.dispatchEvent(new dom.window.Event("change", { bubbles: true }));

    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: settingKey, value: true }
    });

    dom.window.close();
  });

  it("reflects values pushed by the host", async () => {
    const { dom, checkbox, hydrate } = await createSettingsDom({ [settingKey]: true });

    expect(checkbox.checked).toBe(true);
    hydrate({ [settingKey]: false });
    expect(checkbox.checked).toBe(false);

    dom.window.close();
  });
});
