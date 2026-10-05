// Configuration page controller. Loaded by Jellyfin via data-controller="__plugin/HyperionGrabberJs".
// Uses only the globals every supported Jellyfin web client provides: ApiClient and Dashboard.

const HyperionGrabberConfig = {
    pluginUniqueId: '501879a2-6653-4450-b66c-73ba37aa6e3f',
    minPriority: 100,
    maxPriority: 199,
    defaultFramesPerSecond: 25,
    maxFramesPerSecond: 60
};

function readFramesPerSecond(view) {
    return Number.parseInt(view.querySelector('#FramesPerSecond').value, 10);
}

function validateFramesPerSecond(fps) {
    if (!Number.isInteger(fps) || fps < 1 || fps > HyperionGrabberConfig.maxFramesPerSecond) {
        return `Frame rate must be between 1 and ${HyperionGrabberConfig.maxFramesPerSecond}.`;
    }

    return null;
}

function readTarget(view) {
    return {
        Host: view.querySelector('#HyperionHost').value.trim(),
        Port: Number.parseInt(view.querySelector('#HyperionPort').value, 10),
        Priority: Number.parseInt(view.querySelector('#HyperionPriority').value, 10)
    };
}

function validateTarget(target) {
    if (!target.Host) {
        return 'Enter the host name or IP address of your Hyperion server.';
    }

    if (!Number.isInteger(target.Port) || target.Port < 1 || target.Port > 65535) {
        return 'Port must be between 1 and 65535.';
    }

    if (!Number.isInteger(target.Priority) || target.Priority < HyperionGrabberConfig.minPriority || target.Priority > HyperionGrabberConfig.maxPriority) {
        return `Priority must be between ${HyperionGrabberConfig.minPriority} and ${HyperionGrabberConfig.maxPriority}.`;
    }

    return null;
}

function showResult(view, success, message) {
    const result = view.querySelector('#HyperionResult');
    result.textContent = (success ? '✔ ' : '✖ ') + message;
    result.hidden = false;
}

function showPlaybackResult(view, message) {
    const result = view.querySelector('#PlaybackResult');
    result.textContent = message || '';
    result.hidden = !message;
}

function setBusy(view, busy) {
    for (const id of ['#HyperionTestConnection', '#HyperionTestPattern']) {
        view.querySelector(id).disabled = busy;
    }
}

async function postAction(path, body) {
    try {
        return await ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl(path),
            data: JSON.stringify(body),
            contentType: 'application/json',
            dataType: 'json'
        });
    } catch (error) {
        // ApiClient rejects with the fetch Response for HTTP errors; our 400 responses carry a message.
        if (error && typeof error.json === 'function') {
            try {
                const payload = await error.json();
                return { Success: false, Message: payload.Message || payload.title || `Request failed (${error.status}).` };
            } catch {
                return { Success: false, Message: `Request failed (${error.status}).` };
            }
        }

        return { Success: false, Message: 'Request failed. Check that the Jellyfin server is reachable.' };
    }
}

async function runAction(view, path, extra, pendingMessage) {
    const target = readTarget(view);
    const problem = validateTarget(target);
    if (problem) {
        showResult(view, false, problem);
        return;
    }

    setBusy(view, true);
    showResult(view, true, pendingMessage);
    try {
        const response = await postAction(path, Object.assign(target, extra));
        showResult(view, response.Success, response.Message);
    } finally {
        setBusy(view, false);
    }
}

// Jellyfin may format GUIDs with or without dashes; compare them normalized.
function normalizeId(id) {
    return String(id || '').replace(/-/g, '').toLowerCase();
}

// Renders one checkbox per entry: everything Jellyfin offers plus saved selections it no longer lists.
// Names come from Jellyfin clients, so they are only ever set as text.
function renderChoices(list, offered, selected, emptyText) {
    const selectedIds = new Set(selected.map(s => normalizeId(s.Id)));
    const entries = offered.map(o => ({ Id: o.Id, Name: o.Name, Detail: o.Detail }));
    const offeredIds = new Set(entries.map(e => normalizeId(e.Id)));
    for (const saved of selected) {
        if (!offeredIds.has(normalizeId(saved.Id))) {
            entries.push({ Id: saved.Id, Name: saved.Name || saved.Id, Detail: 'not seen recently' });
        }
    }

    list.replaceChildren();
    if (entries.length === 0) {
        const empty = document.createElement('div');
        empty.className = 'fieldDescription';
        empty.textContent = emptyText;
        list.append(empty);
        return;
    }

    for (const entry of entries) {
        // Static markup so Jellyfin upgrades the emby-checkbox (its polyfill breaks createElement(name, { is })).
        const wrapper = document.createElement('div');
        wrapper.innerHTML = '<label><input is="emby-checkbox" type="checkbox" /><span></span></label>';
        const label = wrapper.firstElementChild;
        const input = label.querySelector('input');
        input.dataset.id = entry.Id;
        input.dataset.name = entry.Name;
        input.checked = selectedIds.has(normalizeId(entry.Id));
        label.querySelector('span').textContent = entry.Detail ? `${entry.Name} (${entry.Detail})` : entry.Name;
        list.append(label);
    }
}

function readChoices(list) {
    return Array.from(list.querySelectorAll('input[type="checkbox"]'))
        .filter(input => input.checked)
        .map(input => ({ Id: input.dataset.id, Name: input.dataset.name }));
}

function describeDevice(device) {
    return device.UserName ? `${device.Client}, ${device.UserName}` : device.Client;
}

async function loadClients(view, selectedDevices, selectedUsers) {
    let clients = { Devices: [], Users: [] };
    try {
        clients = await ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('HyperionGrabber/Clients'), dataType: 'json' });
        showPlaybackResult(view, null);
    } catch {
        showPlaybackResult(view, '✖ Could not load the devices and users from Jellyfin. Saved selections are still shown.');
    }

    renderChoices(
        view.querySelector('#PlaybackDeviceList'),
        (clients.Devices || []).map(d => ({ Id: d.Id, Name: d.Name, Detail: describeDevice(d) })),
        selectedDevices,
        'No devices yet. Start playing something on your TV, then press Refresh.');
    renderChoices(
        view.querySelector('#PlaybackUserList'),
        (clients.Users || []).map(u => ({ Id: u.Id, Name: u.Name })),
        selectedUsers,
        'No users yet.');
}

export default function (view) {
    view.addEventListener('viewshow', function () {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(HyperionGrabberConfig.pluginUniqueId).then(function (config) {
            view.querySelector('#HyperionHost').value = config.HyperionHost || '';
            view.querySelector('#HyperionPort').value = config.HyperionPort || 19400;
            view.querySelector('#HyperionPriority').value = config.HyperionPriority || 150;
            view.querySelector('#PlaybackEnabled').checked = config.PlaybackEnabled === true;
            view.querySelector('#FramesPerSecond').value = config.FramesPerSecond || HyperionGrabberConfig.defaultFramesPerSecond;
            return loadClients(view, config.PlaybackDevices || [], config.PlaybackUsers || []);
        }).then(function () {
            Dashboard.hideLoadingMsg();
        }).catch(function () {
            Dashboard.hideLoadingMsg();
            Dashboard.processErrorResponse({ statusText: 'Failed to load the Hyperion Grabber configuration.' });
        });
    });

    view.querySelector('#HyperionTestConnection').addEventListener('click', function () {
        runAction(view, 'HyperionGrabber/TestConnection', {}, 'Connecting…');
    });

    view.querySelector('#HyperionTestPattern').addEventListener('click', function () {
        runAction(view, 'HyperionGrabber/TestPattern', { DurationSeconds: 8 }, 'Sending the test pattern for 8 seconds… look at your TV.');
    });

    view.querySelector('#PlaybackRefresh').addEventListener('click', function () {
        loadClients(view, readChoices(view.querySelector('#PlaybackDeviceList')), readChoices(view.querySelector('#PlaybackUserList')));
    });

    view.querySelector('#HyperionGrabberConfigForm').addEventListener('submit', function (e) {
        e.preventDefault();
        const target = readTarget(view);
        const problem = validateTarget(target);
        if (problem) {
            showResult(view, false, problem);
            return false;
        }

        const playbackEnabled = view.querySelector('#PlaybackEnabled').checked;
        const framesPerSecond = readFramesPerSecond(view);
        const devices = readChoices(view.querySelector('#PlaybackDeviceList'));
        const users = readChoices(view.querySelector('#PlaybackUserList'));
        const fpsProblem = validateFramesPerSecond(framesPerSecond);
        if (fpsProblem) {
            showPlaybackResult(view, '✖ ' + fpsProblem);
            return false;
        }

        if (playbackEnabled && devices.length === 0) {
            showPlaybackResult(view, '✖ Select at least one device, or turn off Follow playback.');
            return false;
        }

        showPlaybackResult(view, null);
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(HyperionGrabberConfig.pluginUniqueId).then(function (config) {
            config.HyperionHost = target.Host;
            config.HyperionPort = target.Port;
            config.HyperionPriority = target.Priority;
            config.PlaybackEnabled = playbackEnabled;
            config.FramesPerSecond = framesPerSecond;
            config.PlaybackDevices = devices;
            config.PlaybackUsers = users;
            return ApiClient.updatePluginConfiguration(HyperionGrabberConfig.pluginUniqueId, config);
        }).then(function (result) {
            Dashboard.processPluginConfigurationUpdateResult(result);
        }).catch(function () {
            Dashboard.hideLoadingMsg();
            Dashboard.processErrorResponse({ statusText: 'Failed to save the Hyperion Grabber configuration.' });
        });

        return false;
    });
}
