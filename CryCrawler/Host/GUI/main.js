$(function () {
    startPollingForStatus();
});

function startPollingForStatus() {
    fetchStatus();
    setInterval(function () {
        if (stillFetching === false || failcount > 5) {
            stillFetching = false;
            failcount = 0;
            fetchStatus();
        }
        else failcount++;
    }, 1000);
}

var failcount = 0;
var stillFetching = false;
function fetchStatus() {
    stillFetching = true;

    $.post("/status", function (data) {
        setStatus(data);
    }).done(function () {
        stillFetching = false;
    }).fail(function () {
        stillFetching = false;
        console.log("Invalid response or connection failed!");
        setStatusText("unreachable");
    });
}

// update webgui
var isActive = false;
function setStatus(data) {
    // set status
    if (data.IsListening === true && data.WorkAvailable) setStatusText("active");
    else if (data.IsListening === true) setStatusText("idle");
    else setStatusText("offline");

    // check host status
    var cclass = "red";
    var ctext = "Disconnected";
    if (data.ConnectedToHost === true) {
        cclass = "green";
        ctext = "Connected";
    }

    // prepare configuration buttons
    let startStop = $("#stop-start-button");
    isActive = data.IsListening;
    if (isActive) {
        startStop.addClass("danger");
        startStop.text("Stop");
    }
    else {
        startStop.removeClass("danger");
        startStop.text("Start");
    }

    // set work mode
    $("#crawler-work-source").html(data.UsingHost === true ? `Host (${data.HostEndpoint}) - <span class='${cclass}'>${ctext}</span>` : "Local");

    // set client id
    $("#crawler-client-id").text(data.UsingHost === true ? data.ClientId : "-");

    // set work count
    $("#crawler-work-count").text(data.WorkCount);

    // set crawled count
    $("#crawler-crawled-count").text(data.CacheCrawledCount);

    // set cached work count
    $("#crawler-cached-work-count").text(data.CacheCount);

    // set RAM usage
    var usage = Math.round((data.UsageRAM / 1024.0) / 1024.0);
    $("#crawler-ram").text(usage + "MB");

    // display clients
    var clientList = $("#list-clients");
    data.Clients.forEach(function (val, i) {
        let id = val.Id;
        let online = val.Online === true ? "Online" : "Offine";
        let lconnected = val.LastConnected;
        let endpoint = val.RemoteEndpoint;

        let onlineClass = val.Online === true ? "green" : "red";

        let exists = false;

        // find if it exists
        clientList.find(".client-item").each(function (ind, el) {
            let c_id = $(el).find(".client-id").text();

            if (id === c_id) {
                exists = true;

                // update values
                let c_online = $(el).find(".client-online");
                c_online.text(online);
                c_online.removeClass("red");
                c_online.removeClass("green");
                c_online.addClass(onlineClass);

                let c_last = $(el).find(".client-last");
                c_last.html(`${lconnected} (${endpoint})`);

                // break loop
                return false;
            }
        });

        if (exists === false) {
            clientList.append($(`<div class="client-item">
                <div class="client-id">${id}</div>
                <div class="client-online ${onlineClass}">${online}</div>
                <div class="client-last">${lconnected} (${endpoint})</div>
            </div>`));
        }
    });

    // check if clientList contains extra clients that need to be removed
    clientList.find(".client-item").each(function (i, el) {
        let c_id = $(el).find(".client-id").text();

        let client = data.Clients.find(function (x) { return x.Id === c_id; });
        if (client === undefined || client === null) {
            $(el).remove();
        }
    });
}

function setStatusText(text) {
    if (text === "active") {
        $("#crawler-status").addClass("active");
        $("#crawler-status").removeClass("offline");
        $("#crawler-status").removeClass("idle");
        $("#crawler-status").text("Active");
    }
    else if (text === "offline") {
        $("#crawler-status").removeClass("active");
        $("#crawler-status").addClass("offline");
        $("#crawler-status").removeClass("idle");
        $("#crawler-status").text("Offline");
    }
    else if (text === "unreachable") {
        $("#crawler-status").removeClass("active");
        $("#crawler-status").addClass("offline");
        $("#crawler-status").removeClass("idle");
        $("#crawler-status").text("Unreachable");
    }
    else {
        $("#crawler-status").removeClass("active");
        $("#crawler-status").removeClass("offline");
        $("#crawler-status").addClass("idle");
        $("#crawler-status").text("Idle");
    }
}

function startStop(self) {
    let btn = $(self);

    updateState({

        IsActive: !isActive

    }, function (s) {

    });
}

function updateState(data, callback) {
    $.post("/state", JSON.stringify(data))
        .done(function (s) {
            if (s.Success === true) callback(s);
            else {
                alert(s.Error);
            }
        }).fail(function (f) {
            console.log(f);
            alert("Lost connection!");
        });
}