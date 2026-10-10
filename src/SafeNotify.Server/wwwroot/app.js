const state = {
  csrf: "",
  email: "",
  organisation: "",
  page: "overview",
  groups: [],
  devices: [],
};
const labels = {
  overview: "Dashboard",
  send: "Send alert",
  requests: "Device requests",
  devices: "Devices",
  groups: "Groups",
  history: "Alert history",
  audit: "Audit log",
  settings: "Settings",
};
const el = (id) => document.getElementById(id);
const escapeHtml = (value) =>
  String(value ?? "").replace(
    /[&<>"']/g,
    (char) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        char
      ],
  );
const time = (value) => (value ? new Date(value).toLocaleString() : "Never");
const toast = (message, error = false) => {
  const node = el("toast");
  node.textContent = message;
  node.classList.toggle("error", error);
  node.classList.add("show");
  setTimeout(() => node.classList.remove("show"), 3200);
};
async function request(path, options = {}) {
  const headers = { ...(options.headers || {}) };
  if (options.body) headers["Content-Type"] = "application/json";
  if (options.method && options.method !== "GET") {
    if (!state.csrf) await getCsrf();
    headers["X-SafeNotify-CSRF"] = state.csrf;
  }
  const response = await fetch(path, {
    ...options,
    headers,
    credentials: "same-origin",
  });
  if (response.status === 204) return null;
  const data = await response.json().catch(() => ({}));
  if (!response.ok) {
    if (response.status === 401) showAccess(false);
    throw new Error(
      data.error || data.message || `Request failed (${response.status})`,
    );
  }
  return data;
}
async function getCsrf() {
  state.csrf = (await requestRaw("/api/security/csrf")).token || "";
}
async function requestRaw(path) {
  const response = await fetch(path, { credentials: "same-origin" });
  return response.json();
}
async function init() {
  try {
    await getCsrf();
    const setup = await requestRaw("/api/setup/status");
    if (!setup.complete) {
      showAccess(true);
      return;
    }
    try {
      const me = await requestRaw("/api/auth/me");
      state.email = me.email;
      showApp();
      return;
    } catch {}
    showAccess(false);
  } catch {
    showAccess(false);
  }
}
function showAccess(setup) {
  el("access").classList.remove("hidden");
  el("app").classList.add("hidden");
  el("accessTitle").textContent = setup
    ? "Set up your organisation"
    : "Administrator sign in";
  el("accessIntro").textContent = setup
    ? "Create the first administrator account for this server."
    : "Sign in to your local alert dashboard.";
  el("accessForm").querySelector('button[type="submit"]').textContent = setup
    ? "Create account"
    : "Sign in";
  el("organisationField").classList.toggle("hidden", !setup);
  el("accessForm").querySelector('[name="password"]').autocomplete = setup
    ? "new-password"
    : "current-password";
  el("accessError").textContent = "";
}
function showApp() {
  el("access").classList.add("hidden");
  el("app").classList.remove("hidden");
  el("adminEmail").textContent = (state.email || "A").slice(0, 1).toUpperCase();
  el("adminEmail").title = state.email;
  go("overview");
}
function initEvents() {
  el("accessForm").addEventListener("submit", submitAccessForm);
  el("nav").addEventListener("click", (event) => {
    const button = event.target.closest("[data-page]");
    if (button) go(button.dataset.page);
  });
  el("logout").addEventListener("click", signOut);
  el("content").addEventListener("click", handleContentClick);
  el("content").addEventListener("change", handleContentChange);
  el("content").addEventListener("input", handleContentInput);
  el("content").addEventListener("submit", handleContentSubmit);
}
async function handleContentClick(event) {
  const button = event.target.closest(
    "[data-action], #lanSetup, #lanToggle, #downloadLanCertificate, #backup",
  );
  if (!button) return;

  const id = button.dataset.action || button.id;
  const recordId = Number(button.dataset.id);
  if (id === "navigate") return go(button.dataset.page);
  if (id === "review-request")
    return reviewRequest(recordId, button.dataset.decision);
  if (id === "rename-device") return renameDevice(recordId);
  if (id === "revoke-device") return revokeDevice(recordId);
  if (id === "delete-group") return deleteGroup(recordId);
  if (id === "confirm-resolution") return confirmResolution(recordId);
  if (id === "delivery-details") return deliveryDetails(recordId);
  if (id === "lanSetup") return setupLan(button);
  if (id === "lanToggle") return toggleLan(button);
  if (id === "downloadLanCertificate") return downloadCertificate();
  if (id === "backup") return createBackup();
}
function handleContentChange(event) {
  if (event.target.id === "audience") updateAudienceTarget();
  if (event.target.dataset.action === "assign-group") {
    assignGroup(Number(event.target.dataset.id), event.target.value);
  }
}
function handleContentInput(event) {
  if (event.target.form?.id !== "alertForm") return;

  const values = new FormData(event.target.form);
  el("previewTitle").textContent = values.get("title") || "Alert title";
  el("previewMessage").textContent =
    values.get("message") || "Enter a message to see the preview.";
  el("preview").className = `preview ${values.get("severity")}`;
}
function handleContentSubmit(event) {
  if (event.target.id === "alertForm") return submitAlert(event);
  if (event.target.id === "groupForm") return submitGroup(event);
  if (event.target.id === "orgForm") return submitOrganisation(event);
}
function updateAudienceTarget() {
  const audience = el("audience");
  if (!audience) return;

  const selectedAudience = audience.value;
  el("targetField").classList.toggle("hidden", selectedAudience === "All");
  const recipients =
    selectedAudience === "Group" ? state.groups : state.devices;
  el("target").innerHTML = recipients.length
    ? recipients
        .map(
          (item) =>
            `<option value="${item.id}">${escapeHtml(item.name)}</option>`,
        )
        .join("")
    : '<option value="">No approved recipients are available</option>';
}
async function submitAlert(event) {
  event.preventDefault();
  const values = new FormData(event.target);
  const severity = values.get("severity");
  const audience = values.get("audience");
  const target =
    audience === "All"
      ? "all approved devices"
      : audience === "Group"
        ? "the selected group"
        : "the selected device";
  if (!confirm(`Send this alert to ${target}?`)) return;

  const body = {
    title: values.get("title"),
    message: values.get("message"),
    severity,
    audience,
    groupId: audience === "Group" ? Number(values.get("targetId")) : null,
    deviceId: audience === "Device" ? Number(values.get("targetId")) : null,
    expiresMinutes: Number(values.get("expiresMinutes")),
    speakMessage: values.get("speakMessage") === "on",
  };

  try {
    const result = await request("/api/admin/alerts", {
      method: "POST",
      body: JSON.stringify(body),
    });
    const deviceLabel = result.recipients === 1 ? "device" : "devices";
    toast(
      `Alert sent to ${result.recipients} ${deviceLabel}: ${result.online} online, ${result.offline} offline.`,
    );
    go("history");
  } catch (error) {
    toast(error.message, true);
  }
}
async function submitGroup(event) {
  event.preventDefault();
  const name = new FormData(event.target).get("name");
  try {
    await request("/api/admin/groups", {
      method: "POST",
      body: JSON.stringify({ name }),
    });
    toast("Group created.");
    groupsPage();
  } catch (error) {
    toast(error.message, true);
  }
}
async function submitOrganisation(event) {
  event.preventDefault();
  const name = new FormData(event.target).get("name");
  try {
    await request("/api/admin/settings/organisation", {
      method: "PUT",
      body: JSON.stringify({ name }),
    });
    el("orgLabel").textContent = name;
    toast("Organisation name saved.");
  } catch (error) {
    toast(error.message, true);
  }
}
async function setupLan(button) {
  if (
    !confirm(
      "SafeNotify will trust a new local certificate authority for this Windows user and add a firewall rule limited to your local network. Continue?",
    )
  )
    return;

  button.disabled = true;
  button.textContent = "Setting up LAN...";
  try {
    const result = await request("/api/admin/network/lan/setup", {
      method: "POST",
    });
    await downloadLanCertificate(result.certificate);
    el("lanSetupStatus").innerHTML =
      `LAN is set up. The public certificate was downloaded. Install it in the Current User Trusted Root store on each client, then open <a href="${escapeHtml(result.address)}">${escapeHtml(result.address)}</a>. The dashboard will open at that HTTPS address shortly.`;
    setTimeout(() => location.assign(result.address), 7000);
  } catch (error) {
    button.disabled = false;
    button.textContent = "Set up LAN automatically";
    toast(error.message, true);
  }
}
async function toggleLan(button) {
  const enabled = button.dataset.enabled !== "true";
  try {
    await request("/api/admin/network/lan", {
      method: "PUT",
      body: JSON.stringify({ enabled }),
    });
    button.disabled = true;
    button.textContent = "Restarting server...";
    const destination = enabled
      ? button.dataset.address
      : `http://127.0.0.1:${button.dataset.port}`;
    setTimeout(() => location.assign(destination), 7000);
  } catch (error) {
    toast(error.message, true);
  }
}
async function downloadCertificate() {
  try {
    await downloadLanCertificate();
    toast("SafeNotify LAN certificate downloaded.");
  } catch (error) {
    toast(error.message, true);
  }
}
async function createBackup() {
  try {
    const result = await request("/api/admin/backup", { method: "POST" });
    toast(`Backup created: ${result.file}`);
  } catch (error) {
    toast(error.message, true);
  }
}
async function submitAccessForm(event) {
  event.preventDefault();
  const form = new FormData(event.currentTarget);
  const setup = !el("organisationField").classList.contains("hidden");
  try {
    const result = await request(setup ? "/api/setup" : "/api/auth/login", {
      method: "POST",
      body: JSON.stringify(Object.fromEntries(form.entries())),
    });
    state.email = result.email;
    await getCsrf();
    showApp();
  } catch (error) {
    el("accessError").textContent = error.message;
  }
}
async function signOut() {
  try {
    await request("/api/auth/logout", { method: "POST" });
  } catch {}
  state.csrf = "";
  await init();
}
async function go(page) {
  state.page = page;
  el("pageTitle").textContent = labels[page];
  document
    .querySelectorAll("#nav [data-page]")
    .forEach((button) =>
      button.classList.toggle("active", button.dataset.page === page),
    );
  try {
    await render(page);
  } catch (error) {
    el("content").innerHTML =
      `<section class="panel"><div class="warning-note">${escapeHtml(error.message)}</div></section>`;
  }
}
async function render(page) {
  if (page === "overview") return overview();
  if (page === "send") return sendPage();
  if (page === "requests") return requestsPage();
  if (page === "devices") return devicesPage();
  if (page === "groups") return groupsPage();
  if (page === "history") return historyPage();
  if (page === "audit") return auditPage();
  if (page === "settings") return settingsPage();
}
async function overview() {
  const data = await request("/api/admin/overview");
  const org = await request("/api/admin/settings");
  state.organisation = org.organisation;
  el("orgLabel").textContent = org.organisation || "Organisation";
  el("content").innerHTML = `<div class="page-intro">
      <div>
        <h2>${escapeHtml(org.organisation)} dashboard</h2>
        <p>Device access and recent alerts.</p>
      </div>
      <button class="button primary" data-action="navigate" data-page="send">Compose alert</button>
    </div>
    <div class="stats">
      ${stat("Approved devices", data.approvedDevices, "Active registrations")}
      ${stat("Connected now", data.connectedDevices, "Devices online")}
      ${stat("Pending requests", data.pendingRequests, "Awaiting approval")}
      ${stat("Alerts sent", data.totalAlerts, "All time")}
    </div>
    <section class="panel">
      <div class="panel-heading">
        <h3>Recent alerts</h3>
        <button class="link-button" data-action="navigate" data-page="history">View alert history</button>
      </div>
      ${alertTable(data.recentAlerts)}
    </section>
    <section class="panel">
      <div class="panel-heading">
        <h3>Server status</h3>
        <span class="status Approved">${escapeHtml(data.server)}</span>
      </div>
      <div class="detail-list">
        <div class="detail-row"><span>Organisation</span><strong>${escapeHtml(org.organisation)}</strong></div>
        <div class="detail-row"><span>Local time</span><strong>${time(data.now)}</strong></div>
        <div class="detail-row"><span>Approved devices</span><strong>${data.approvedDevices}</strong></div>
      </div>
    </section>`;
  el("requestBadge").textContent = data.pendingRequests;
  el("requestBadge").classList.toggle("hidden", data.pendingRequests === 0);
}
function stat(title, value, foot) {
  return `<div class="stat">
    <div class="stat-top"><span>${title}</span></div>
    <div class="stat-value">${value}</div>
    <div class="stat-foot">${foot}</div>
  </div>`;
}
function alertTable(items) {
  if (!items.length) return '<div class="empty">No alerts have been sent.</div>';
  const rows = items
    .map(
      (item) => `<tr>
        <td><strong>${escapeHtml(item.title)}</strong></td>
        <td><span class="status ${escapeHtml(item.severity)}">${escapeHtml(item.severity)}</span></td>
        <td>${item.recipients}</td>
        <td>${time(item.createdAt)}</td>
      </tr>`,
    )
    .join("");

  return `<div class="table-wrap">
    <table>
      <thead><tr><th>Alert</th><th>Priority</th><th>Recipients</th><th>Sent</th></tr></thead>
      <tbody>${rows}</tbody>
    </table>
  </div>`;
}
async function sendPage() {
  const [groups, devices] = await Promise.all([
    request("/api/admin/groups"),
    request("/api/admin/devices"),
  ]);
  state.groups = groups;
  state.devices = devices.filter((item) => item.status === "Approved");
  el("content").innerHTML = `<div class="page-intro">
      <div>
        <h2>Compose an alert</h2>
        <p>Choose who should receive the alert, then write the message.</p>
      </div>
    </div>
    <div class="form-grid">
      <section class="panel">
        <form id="alertForm" class="form-grid">
          <div class="field full">
            <label for="title">Title</label>
            <input id="title" name="title" maxlength="120" required placeholder="For example, Staff meeting at 2 pm">
          </div>
          <div class="field full">
            <label>Priority</label>
            <div class="radio-row">
              <label class="severity-option">
                <input type="radio" name="severity" value="message" checked>
                <span>Message<small>Routine information</small></span>
              </label>
              <label class="severity-option warning">
                <input type="radio" name="severity" value="warning">
                <span>Warning<small>Action needed</small></span>
              </label>
              <label class="severity-option emergency">
                <input type="radio" name="severity" value="emergency">
                <span>Emergency<small>Respond now</small></span>
              </label>
            </div>
          </div>
          <div class="field full">
            <label for="message">Message</label>
            <textarea id="message" name="message" maxlength="4000" required placeholder="What should recipients know or do?"></textarea>
          </div>
          <div class="field full">
            <label class="speak-option">
              <input name="speakMessage" type="checkbox">
              <span>
                <strong>Read the alert aloud</strong>
                <small>Messages and warnings are spoken after their sound. For emergencies, the alarm lowers while the alert is read, then returns to full volume.</small>
              </span>
            </label>
          </div>
          <div class="field">
            <label for="audience">Recipients</label>
            <select id="audience" name="audience">
              <option value="All">All approved devices</option>
              <option value="Group">A group</option>
              <option value="Device">One device</option>
            </select>
          </div>
          <div class="field">
            <label for="expires">Alert expires after</label>
            <select id="expires" name="expiresMinutes">
              <option value="15">15 minutes</option>
              <option value="60" selected>1 hour</option>
              <option value="240">4 hours</option>
              <option value="1440">24 hours</option>
            </select>
          </div>
          <div id="targetField" class="field full hidden">
            <label for="target">Select recipients</label>
            <select id="target" name="targetId"></select>
          </div>
          <div class="field full">
            <label>Preview</label>
            <div id="preview" class="preview">
              <strong id="previewTitle">Alert title</strong>
              <p id="previewMessage">Enter a message to see the preview.</p>
            </div>
          </div>
          <div class="field full actions">
            <button class="button primary" type="submit">Send alert</button>
            <span class="subtle">Every alert is recorded in the audit log.</span>
          </div>
        </form>
      </section>
      <aside>
        <div class="warning-note">Emergency alerts should supplement your organisation's established safety procedures. They do not replace certified alarms or emergency services.</div>
      </aside>
    </div>`;
  updateAudienceTarget();
}
async function requestsPage() {
  const items = await request("/api/admin/requests");
  const pending = items.filter((item) => item.status === "Pending");
  const pendingRows = pending
    .map(
      (item) => `<tr>
        <td><strong>${escapeHtml(item.deviceName)}</strong></td>
        <td>${time(item.requestedAt)}</td>
        <td>
          <div class="actions">
            <button class="button primary small" data-action="review-request" data-id="${item.id}" data-decision="approve">Approve</button>
            <button class="button danger small" data-action="review-request" data-id="${item.id}" data-decision="reject">Reject</button>
          </div>
        </td>
      </tr>`,
    )
    .join("");
  const decidedRows = items
    .filter((item) => item.status !== "Pending")
    .slice(0, 20)
    .map(
      (item) => `<tr>
        <td>${escapeHtml(item.deviceName)}</td>
        <td><span class="status ${escapeHtml(item.status)}">${escapeHtml(item.status)}</span></td>
        <td>${time(item.requestedAt)}</td>
      </tr>`,
    )
    .join("");

  el("content").innerHTML = `<div class="page-intro">
      <div>
        <h2>Review access requests</h2>
        <p>Only approved devices can receive organisation alerts.</p>
      </div>
    </div>
    <section class="panel">
      <div class="panel-heading">
        <h3>Pending requests</h3>
        <small>${pending.length} waiting</small>
      </div>
      ${
        pending.length
          ? `<div class="table-wrap">
            <table>
              <thead><tr><th>Device</th><th>Request received</th><th>Actions</th></tr></thead>
              <tbody>${pendingRows}</tbody>
            </table>
          </div>`
          : '<div class="empty">There are no device requests to review.</div>'
      }
    </section>
    <section class="panel">
      <div class="panel-heading"><h3>Reviewed requests</h3></div>
      ${
        decidedRows
          ? `<div class="table-wrap">
            <table>
              <thead><tr><th>Device</th><th>Status</th><th>Requested</th></tr></thead>
              <tbody>${decidedRows}</tbody>
            </table>
          </div>`
          : '<div class="empty">No requests have been reviewed.</div>'
      }
    </section>`;
}
async function reviewRequest(id, decision) {
  if (decision === "reject" && !confirm("Reject this device request?")) return;
  try {
    await request(`/api/admin/requests/${id}/${decision}`, { method: "POST" });
    toast(
      decision === "approve"
        ? "Device approved. It will connect when it checks in."
        : "Device request rejected.",
    );
    requestsPage();
  } catch (error) {
    toast(error.message, true);
  }
}
async function devicesPage() {
  const [items, groups] = await Promise.all([
    request("/api/admin/devices"),
    request("/api/admin/groups"),
  ]);
  state.devices = items;
  state.groups = groups;
  const rows = items.map((item) => deviceRow(item, groups)).join("");
  el("content").innerHTML = `<div class="page-intro">
      <div>
        <h2>Devices</h2>
        <p>Check approval, connection, group and last contact for each device.</p>
      </div>
    </div>
    <section class="panel">
      <div class="panel-heading">
        <h3>Registered devices</h3>
        <small>${items.length} total</small>
      </div>
      ${
        items.length
          ? `<div class="table-wrap">
            <table>
              <thead><tr><th>Device</th><th>Approval</th><th>Connection</th><th>Group</th><th>Last seen</th><th>Actions</th></tr></thead>
              <tbody>${rows}</tbody>
            </table>
          </div>`
          : '<div class="empty">No devices are registered.</div>'
      }
    </section>`;
}
function deviceRow(item, groups) {
  const groupOptions = groups
    .map(
      (group) =>
        `<option value="${group.id}" ${group.name === item.group ? "selected" : ""}>${escapeHtml(group.name)}</option>`,
    )
    .join("");
  const actions =
    item.status === "Approved"
      ? `<button class="button outline small" data-action="rename-device" data-id="${item.id}">Rename</button>
       <select aria-label="Assign group" data-action="assign-group" data-id="${item.id}" class="filter-row">
         <option value="">Unassigned</option>${groupOptions}
       </select>
       <button class="button danger small" data-action="revoke-device" data-id="${item.id}">Revoke</button>`
      : "-";

  return `<tr>
    <td><strong>${escapeHtml(item.name)}</strong><div class="subtle">Added ${time(item.createdAt)}</div></td>
    <td><span class="status ${escapeHtml(item.status)}">${escapeHtml(item.status)}</span></td>
    <td>${item.online ? '<span class="status Approved">Online</span>' : "Offline"}</td>
    <td>${escapeHtml(item.group || "Unassigned")}</td>
    <td>${time(item.lastSeenAt)}</td>
    <td><div class="actions">${actions}</div></td>
  </tr>`;
}
async function renameDevice(id) {
  const current = state.devices.find((item) => item.id === id)?.name || "";
  const name = prompt("Device name", current);
  if (name === null || name.trim() === current) return;
  try {
    await request(`/api/admin/devices/${id}`, {
      method: "PATCH",
      body: JSON.stringify({ name: name.trim() }),
    });
    toast("Device renamed.");
    devicesPage();
  } catch (error) {
    toast(error.message, true);
  }
}
async function assignGroup(id, value) {
  try {
    await request(`/api/admin/devices/${id}/group`, {
      method: "PUT",
      body: JSON.stringify({ groupId: value ? Number(value) : null }),
    });
    toast("Device group updated.");
  } catch (error) {
    toast(error.message, true);
    devicesPage();
  }
}
async function revokeDevice(id) {
  if (!confirm("Revoke this device? Its saved credentials will stop working."))
    return;
  try {
    await request(`/api/admin/devices/${id}/revoke`, { method: "POST" });
    toast("Device access revoked.");
    devicesPage();
  } catch (error) {
    toast(error.message, true);
  }
}
async function groupsPage() {
  const groups = await request("/api/admin/groups");
  state.groups = groups;
  const cards = groups
    .map(
      (group) => `<section class="panel group-card">
        <div>
          <strong>${escapeHtml(group.name)}</strong>
        <small>${group.deviceCount} approved ${group.deviceCount === 1 ? "device" : "devices"}</small>
        </div>
        <button class="button danger small" data-action="delete-group" data-id="${group.id}">Delete</button>
      </section>`,
    )
    .join("");

  el("content").innerHTML = `<div class="page-intro">
      <div>
        <h2>Device groups</h2>
        <p>Use groups for departments, buildings or teams.</p>
      </div>
    </div>
    <section class="panel">
      <div class="panel-heading"><h3>Create a group</h3></div>
      <form id="groupForm" class="inline-form">
        <input name="name" minlength="2" maxlength="80" required placeholder="For example, Main building">
        <button class="button primary">Add group</button>
      </form>
    </section>
    <div class="card-grid">
      ${groups.length ? cards : '<section class="panel empty">No device groups have been created.</section>'}
    </div>`;
}
async function deleteGroup(id) {
  const name =
    state.groups.find((item) => item.id === id)?.name || "this group";
  if (
    !confirm(
      `Delete the group “${name}”? Devices will remain registered without a group.`,
    )
  )
    return;
  try {
    await request(`/api/admin/groups/${id}`, { method: "DELETE" });
    toast("Group deleted.");
    groupsPage();
  } catch (error) {
    toast(error.message, true);
  }
}
async function historyPage() {
  const items = await request("/api/admin/alerts");
  const rows = items
    .map((item) => {
      const resolutionAction =
        item.severity === "emergency" && item.resolved < item.recipients
          ? `<button class="button warning small" data-action="confirm-resolution" data-id="${item.id}">Confirm resolution</button>`
          : "";

      return `<tr>
        <td><strong>${escapeHtml(item.title)}</strong><div class="subtle">${escapeHtml(item.message).slice(0, 110)}</div></td>
        <td><span class="status ${escapeHtml(item.severity)}">${escapeHtml(item.severity)}</span></td>
        <td>${escapeHtml(item.audience)}</td>
        <td>${item.recipients}</td>
        <td>${item.delivered}</td>
        <td>${item.acknowledged}</td>
        <td>${time(item.expiresAt)}</td>
        <td>${resolutionAction}<button class="button outline small" data-action="delivery-details" data-id="${item.id}">Details</button></td>
      </tr>`;
    })
    .join("");

  el("content").innerHTML = `<div class="page-intro">
      <div>
        <h2>Alert history</h2>
        <p>See who received and acknowledged each alert.</p>
      </div>
      <button class="button primary" data-action="navigate" data-page="send">Compose alert</button>
    </div>
    <section class="panel">
      <div class="panel-heading">
        <h3>Sent alerts</h3>
        <small>${items.length} alerts</small>
      </div>
      ${
        items.length
          ? `<div class="table-wrap">
            <table>
              <thead><tr><th>Alert</th><th>Priority</th><th>Audience</th><th>Recipients</th><th>Delivered</th><th>Acknowledged</th><th>Expires</th><th></th></tr></thead>
              <tbody>${rows}</tbody>
            </table>
          </div>`
          : '<div class="empty">No alerts have been sent.</div>'
      }
    </section>`;
}
async function deliveryDetails(id) {
  try {
    const items = await request(`/api/admin/alerts/${id}/deliveries`);
    const lines = items
      .map(
        (item) =>
          `${item.device}: ${item.status}${item.acknowledgedAt ? `; acknowledged ${time(item.acknowledgedAt)}` : ""}${item.resolvedAt ? `; resolved ${time(item.resolvedAt)}` : ""}`,
      )
      .join("\n");
    alert(lines || "No recipients recorded.");
  } catch (error) {
    toast(error.message, true);
  }
}
async function auditPage() {
  const items = await request("/api/admin/audit");
  const rows = items
    .map(
      (item) => `<tr>
        <td>${time(item.createdAt)}</td>
        <td><strong>${escapeHtml(item.action)}</strong></td>
        <td>${escapeHtml(item.details)}</td>
        <td>${escapeHtml(item.administrator || item.device || "System")}</td>
      </tr>`,
    )
    .join("");

  el("content").innerHTML = `<div class="page-intro">
      <div>
        <h2>Audit log</h2>
        <p>Administrator actions and recipient acknowledgements.</p>
      </div>
    </div>
    <section class="panel">
      <div class="panel-heading">
        <h3>Recent activity</h3>
        <small>Showing up to 500 entries</small>
      </div>
      ${
        items.length
          ? `<div class="table-wrap">
            <table>
              <thead><tr><th>When</th><th>Action</th><th>Details</th><th>Actor</th></tr></thead>
              <tbody>${rows}</tbody>
            </table>
          </div>`
          : '<div class="empty">No activity has been recorded.</div>'
      }
    </section>`;
}
async function downloadLanCertificate(encodedCertificate) {
  let blob;
  if (encodedCertificate) {
    const binary = atob(encodedCertificate);
    const bytes = Uint8Array.from(binary, (character) =>
      character.charCodeAt(0),
    );
    blob = new Blob([bytes], { type: "application/pkix-cert" });
  } else {
    const response = await fetch("/api/admin/network/lan/certificate", {
      credentials: "same-origin",
    });
    if (!response.ok)
      throw new Error("Could not download the LAN certificate.");
    blob = await response.blob();
  }
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = "SafeNotify-LAN.cer";
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
async function settingsPage() {
  const [settings, network] = await Promise.all([
    request("/api/admin/settings"),
    request("/api/admin/network/lan"),
  ]);
  state.organisation = settings.organisation;
  const address = network.addresses.length
    ? `https://${network.addresses[0]}:${network.port}`
    : "No LAN address is available";
  const lanAction = network.certificateConfigured
    ? `<button id="lanToggle" data-enabled="${network.enabled}" data-address="${escapeHtml(address)}" data-port="${network.port}" class="button ${network.enabled ? "danger" : "primary"}">${network.enabled ? "Turn LAN access off" : "Enable LAN access"}</button>`
    : '<button id="lanSetup" class="button primary">Set up LAN automatically</button>';
  const download = network.certificateDownloadAvailable
    ? '<button id="downloadLanCertificate" class="button secondary">Download trust certificate</button>'
    : "";
  const certificateMessage = network.certificateConfigured
    ? "HTTPS is configured. Install the public certificate on each Windows client before connecting."
    : "Automatic setup creates the HTTPS certificate, enables LAN access and limits the firewall rule to your local network. Download the public certificate and install it on each client.";
  const lanStatus = network.certificateConfigured
    ? `LAN access is ${network.enabled ? "on" : "off"}.`
    : "LAN access has not been configured.";
  const addressText = network.enabled ? address : "LAN access is off";
  const certificateStatus = network.certificateConfigured
    ? "Configured"
    : "Not configured";

  el("content").innerHTML = `<div class="page-intro">
      <div>
        <h2>Organisation settings</h2>
        <p>Manage LAN access, the organisation name and server data.</p>
      </div>
    </div>
    <section class="panel">
      <div class="panel-heading">
        <h3>LAN access</h3>
        <div class="actions">${download}${lanAction}</div>
      </div>
      <p class="subtle">${escapeHtml(certificateMessage)}</p>
      <div id="lanSetupStatus" class="warning-note">${lanStatus}</div>
      <div class="detail-list">
        <div class="detail-row"><span>Address to give approved users</span><strong id="lanAddress">${escapeHtml(addressText)}</strong></div>
        <div class="detail-row"><span>Port</span><strong>${network.port}</strong></div>
        <div class="detail-row"><span>HTTPS certificate</span><strong>${certificateStatus}</strong></div>
      </div>
      <p class="subtle">Share this address only with people connected to your organisation's local network. The firewall rule is limited to the local subnet. Keep the dashboard off the public internet.</p>
    </section>
    <section class="panel">
      <div class="panel-heading"><h3>Organisation name</h3></div>
      <form id="orgForm" class="inline-form">
        <input name="name" minlength="2" maxlength="120" required value="${escapeHtml(settings.organisation)}">
        <button class="button primary">Save name</button>
      </form>
    </section>
    <section class="panel">
      <div class="panel-heading"><h3>Administrator account</h3></div>
      <div class="detail-row"><span>Signed in as</span><strong>${escapeHtml(settings.administrator)}</strong></div>
    </section>
    <section class="panel">
      <div class="panel-heading">
        <h3>Database backup</h3>
        <button id="backup" class="button secondary">Create backup</button>
      </div>
      <p class="subtle">Saves a consistent database copy in the server data folder. Keep the backup and its matching data-protection keys in secure storage.</p>
      <div class="warning-note">Keep the database, data protection keys and backups restricted to authorised server administrators.</div>
    </section>`;
}
async function confirmResolution(id) {
  if (
    !confirm(
      "Confirm that this emergency has been resolved for all recorded recipients?",
    )
  )
    return;
  try {
    const result = await request(`/api/admin/alerts/${id}/resolve`, {
      method: "POST",
    });
    toast(`Resolution confirmed for ${result.recipients} recipients.`);
    historyPage();
  } catch (error) {
    toast(error.message, true);
  }
}
initEvents();
init();
