// Configuration page controller. Loaded by Jellyfin via data-controller="__plugin/HyperionGrabberJs".
// Uses only the globals every supported Jellyfin web client provides: ApiClient and Dashboard.

const HyperionGrabberConfig = {
    pluginUniqueId: '501879a2-6653-4450-b66c-73ba37aa6e3f',
    minPriority: 100,
    maxPriority: 199
};

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

export default function (view) {
    view.addEventListener('viewshow', function () {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(HyperionGrabberConfig.pluginUniqueId).then(function (config) {
            view.querySelector('#HyperionHost').value = config.HyperionHost || '';
            view.querySelector('#HyperionPort').value = config.HyperionPort || 19400;
            view.querySelector('#HyperionPriority').value = config.HyperionPriority || 150;
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

    view.querySelector('#HyperionGrabberConfigForm').addEventListener('submit', function (e) {
        e.preventDefault();
        const target = readTarget(view);
        const problem = validateTarget(target);
        if (problem) {
            showResult(view, false, problem);
            return false;
        }

        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(HyperionGrabberConfig.pluginUniqueId).then(function (config) {
            config.HyperionHost = target.Host;
            config.HyperionPort = target.Port;
            config.HyperionPriority = target.Priority;
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
