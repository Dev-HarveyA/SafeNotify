const state = {
  server: localStorage.getItem("safenotify.server") || location.origin,
  installation: localStorage.getItem("safenotify.installation") || crypto.randomUUID(),
  claim: localStorage.getItem("safenotify.claim") || "",
  requestId: Number(localStorage.getItem("safenotify.requestId")) || 0,
  credential: localStorage.getItem("safenotify.credential") || "",
  activeAlert: null,
  audio: null,
  oscillator: null,
  alarmGain: null,
};

const elements = Object.fromEntries(
  [
    "server",
    "connect-form",
    "join",
    "notifications",
    "organisation",
    "status",
    "connection",
    "alert-dialog",
    "alert-card",
    "alert-level",
    "alert-title",
    "alert-message",
    "acknowledge",
    "ack-status",
  ].map((id) => [id, document.getElementById(id)]),
);

elements.server.value = state.server;
localStorage.setItem("safenotify.installation", state.installation);

function updateStatus(message, online = false) {
  elements.status.textContent = message;
  elements.connection.textContent = online ? "Connected" : "Not connected";
  elements.connection.classList.toggle("online", online);
}

function saveState() {
  localStorage.setItem("safenotify.server", state.server);
  localStorage.setItem("safenotify.installation", state.installation);
  localStorage.setItem("safenotify.claim", state.claim);
  localStorage.setItem("safenotify.requestId", String(state.requestId || ""));
  localStorage.setItem("safenotify.credential", state.credential);
}

async function api(path, options = {}) {
  const headers = new Headers(options.headers || {});
  if (options.body) headers.set("Content-Type", "application/json");
  if (state.credential) headers.set("Authorization", `Bearer ${state.credential}`);
  const response = await fetch(new URL(path, state.server), { ...options, headers, cache: "no-store" });
  const data = response.status === 204 ? null : await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(data?.error || data?.message || `Server returned ${response.status}.`);
  return data;
}

function validServer(value) {
  try {
    const url = new URL(value);
  const secure = url.protocol === "https:";
  const local = url.protocol === "http:" && ["localhost", "127.0.0.1", "[::1]"].includes(url.hostname);
  return (secure || local) && url.pathname === "/" && !url.search && !url.hash;
  } catch {
    return false;
  }
}

elements["connect-form"].addEventListener("submit", async (event) => {
  event.preventDefault();
  armAudio();
  state.server = elements.server.value.trim().replace(/\/$/, "");
  if (!validServer(state.server)) {
    updateStatus("Enter the HTTPS address shown by your SafeNotify administrator.");
    return;
  }
  saveState();
  elements.join.hidden = true;
  updateStatus("Checking the server…");
  try {
    const result = await api("/api/discovery");
    elements.organisation.textContent = result.organisation;
    if (state.credential) {
      await api("/api/client/session");
      updateStatus("This device is approved to receive organisation alerts.", true);
      elements.notifications.hidden = !window.Notification || Notification.permission === "granted";
      return;
    }
    if (state.requestId) {
      updateStatus("Access request sent. Waiting for administrator approval.", true);
      return;
    }
    elements.join.hidden = false;
    updateStatus("Organisation found. Request access to receive alerts.", true);
  } catch (error) {
    updateStatus(error.message);
  }
});

elements.join.addEventListener("click", async () => {
  armAudio();
  elements.join.disabled = true;
  state.claim ||= bytesToBase64(crypto.getRandomValues(new Uint8Array(48)));
  try {
    const result = await api("/api/client/join", {
      method: "POST",
      body: JSON.stringify({
        installationId: state.installation,
        deviceName: navigator.userAgentData?.platform || "Chromebook",
        claimToken: state.claim,
      }),
    });
    state.requestId = result.requestId;
    saveState();
    elements.join.hidden = true;
    updateStatus("Access request sent. Waiting for administrator approval.", true);
  } catch (error) {
    updateStatus(error.message);
    elements.join.disabled = false;
  }
});

elements.notifications.addEventListener("click", async () => {
  if (!("Notification" in window)) return;
  const permission = await Notification.requestPermission();
  elements.notifications.hidden = permission === "granted";
  updateStatus(permission === "granted" ? "Desktop alerts are enabled." : "Desktop notifications were not enabled.", true);
});

elements.acknowledge.addEventListener("click", async () => {
  const alert = state.activeAlert;
  if (!alert) return;
  elements.acknowledge.disabled = true;
  elements.acknowledge.textContent = "Recording acknowledgement…";
  try {
    await api(`/api/client/deliveries/${alert.id}/acknowledge`, { method: "POST" });
    stopAlarm();
    elements["alert-dialog"].close();
    state.activeAlert = null;
  } catch {
    elements["ack-status"].textContent = "Could not record the acknowledgement. Check the connection and try again.";
  } finally {
    elements.acknowledge.disabled = false;
    elements.acknowledge.textContent = alert.severity === "emergency" ? "Acknowledge emergency" : "Acknowledge";
  }
});

elements["alert-dialog"].addEventListener("cancel", (event) => event.preventDefault());

function bytesToBase64(bytes) {
  return btoa(String.fromCharCode(...bytes));
}

function playTones(severity) {
  const AudioContextType = window.AudioContext || window.webkitAudioContext;
  if (!AudioContextType) return;
  state.audio ||= new AudioContextType();
  state.audio.resume();
  if (severity === "emergency") {
    state.oscillator = state.audio.createOscillator();
    const gain = state.audio.createGain();
    state.oscillator.type = "sawtooth";
    state.oscillator.frequency.value = 440;
    gain.gain.value = 0.25;
    state.alarmGain = gain.gain;
    state.oscillator.connect(gain);
    gain.connect(state.audio.destination);
    state.oscillator.start();
    return;
  }
  const count = severity === "warning" ? 3 : 1;
  for (let index = 0; index < count; index++) {
    const start = state.audio.currentTime + index * 0.2;
    const oscillator = state.audio.createOscillator();
    const gain = state.audio.createGain();
    oscillator.frequency.value = severity === "warning" ? 1000 : 880;
    gain.gain.setValueAtTime(0.0001, start);
    gain.gain.linearRampToValueAtTime(0.22, start + 0.01);
    gain.gain.setValueAtTime(0.22, start + 0.1);
    gain.gain.exponentialRampToValueAtTime(0.0001, start + 0.14);
    oscillator.connect(gain);
    gain.connect(state.audio.destination);
    oscillator.start(start);
    oscillator.stop(start + 0.15);
  }
}

function armAudio() {
  const AudioContextType = window.AudioContext || window.webkitAudioContext;
  if (!AudioContextType) return;
  state.audio ||= new AudioContextType();
  state.audio.resume();
}

function stopAlarm() {
  if (!state.oscillator) return;
  state.oscillator.stop();
  state.oscillator.disconnect();
  state.oscillator = null;
  state.alarmGain = null;
}

async function speakAlert(alert) {
  if (!alert.speakMessage || !("speechSynthesis" in window)) return;
  if (alert.severity !== "emergency") {
    const pause = alert.severity === "warning" ? 900 : 500;
    await new Promise((resolve) => setTimeout(resolve, pause));
  }
  if (alert.severity === "emergency" && state.alarmGain)
    state.alarmGain.setValueAtTime(0.08, state.audio.currentTime);
  await new Promise((resolve) => {
    const speech = new SpeechSynthesisUtterance(`${alert.title}. ${alert.message}`);
    speech.onend = resolve;
    speech.onerror = resolve;
    window.speechSynthesis.speak(speech);
  });
  if (alert.severity === "emergency" && state.alarmGain)
    state.alarmGain.setValueAtTime(0.25, state.audio.currentTime);
}

async function showAlert(alert) {
  if (state.activeAlert || elements["alert-dialog"].open) return;
  state.activeAlert = alert;
  elements["alert-level"].textContent = alert.severity === "emergency" ? "EMERGENCY ALERT" : alert.severity.toUpperCase();
  elements["alert-title"].textContent = alert.title;
  elements["alert-message"].textContent = alert.message;
  elements["alert-card"].className = `alert-card ${alert.severity}`;
  elements.acknowledge.textContent = alert.severity === "emergency" ? "Acknowledge emergency" : "Acknowledge";
  elements["ack-status"].textContent = alert.severity === "emergency"
    ? "The alarm continues until you acknowledge this alert."
    : "Acknowledge to close this alert.";
  elements["alert-dialog"].showModal();
  if (document.hidden && "Notification" in window && Notification.permission === "granted") {
    const notice = new Notification(alert.title, {
      body: alert.message,
      tag: `safenotify-${alert.id}`,
      requireInteraction: true,
    });
    notice.onclick = () => { window.focus(); notice.close(); };
  }
  playTones(alert.severity);
  await speakAlert(alert);
}

async function poll() {
  if (state.requestId && state.claim) {
    try {
      const result = await api(`/api/client/join/${state.requestId}/status`, {
        method: "POST",
        body: JSON.stringify({ claimToken: state.claim }),
      });
      if (result.status === "Approved" && result.credential) {
        state.credential = result.credential;
        state.requestId = 0;
        state.claim = "";
        saveState();
        updateStatus("Access approved. This device can receive alerts.", true);
      } else if (result.status === "Rejected") {
        state.requestId = 0;
        state.claim = "";
        saveState();
        elements.join.hidden = false;
        elements.join.disabled = false;
        updateStatus("Access request declined. Contact your administrator.");
      }
    } catch {
      updateStatus("Waiting for administrator approval. The server will be checked again.");
    }
  }
  if (!state.credential) return;
  try {
    const alerts = await api("/api/client/alerts");
    for (const alert of alerts) {
      if (Date.parse(alert.expiresAt) > Date.now()) await showAlert(alert);
    }
  } catch (error) {
    if (error.message.includes("401")) {
      state.credential = "";
      saveState();
      updateStatus("This device is no longer approved. Request access again.");
      elements.join.hidden = false;
    } else {
      updateStatus("Server unavailable. SafeNotify will keep trying.");
    }
  }
}

setInterval(poll, 7000);
poll();

async function restoreConnection() {
  if (!state.credential && !state.requestId) return;
  try {
    const result = await api("/api/discovery");
    elements.organisation.textContent = result.organisation;
    if (state.credential) {
      await api("/api/client/session");
      updateStatus("This device is approved to receive organisation alerts.", true);
      elements.notifications.hidden = !window.Notification || Notification.permission === "granted";
      return;
    }
    updateStatus("Access request sent. Waiting for administrator approval.", true);
  } catch (error) {
    updateStatus(error.message);
  }
}

restoreConnection();

if ("serviceWorker" in navigator) navigator.serviceWorker.register("/chromebook/sw.js", { scope: "/chromebook/" });
