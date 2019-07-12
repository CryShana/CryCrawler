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
    if (data.IsActive === true && data.IsWorking) setStatusText("active");
    else if (data.IsActive === true) setStatusText("idle");
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
    startStop.removeClass("disabled");
    isActive = data.IsActive;
    if (isActive) {
        startStop.addClass("danger");
        startStop.text("Stop Crawler");
    }
    else {
        startStop.removeClass("danger");
        startStop.text("Start Crawler");
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

    // set tasks
    $("table#table-tasks").empty();

    for (let i = 1; i <= data.TaskCount; i++) {
        var currentUrl = data.CurrentTasks[i];

        let td1 = $("<td></td>");
        td1.css("font-weight", "bold");
        td1.css("width", "15px");
        td1.text(i);

        let td2 = $("<td></td>");
        td2.css("white-space", "nowrap");

        if (currentUrl === null) {
            td2.css("color", "red");
            td2.text("Inactive");
        }
        else td2.text(currentUrl);

        let tr = $("<tr></tr>");
        tr.append(td1);
        tr.append(td2);

        $("table#table-tasks").append(tr);
    }

    // set logs
    var l = data.RecentDownloads.length;
    for (let i = 0; i < l; i++) {
        var el = data.RecentDownloads[i];
        if (currentLogs.indexOf(el.FilePath) === -1) addDownloadLog(el);
    }
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


currentLogs = [];
var maxLogs = 50;
function addDownloadLog(el) {
    let td1 = $("<td></td>");
    td1.css("font-weight", "bold");
    td1.css("width", "170px");
    td1.text(el.DownloadedTime);

    let td2 = $("<td class='midcol'></td>");
    td2.css("color", "#848484");
    td2.css("width", "80px");
    td2.text(el.Size);

    let td3 = $("<td class='clickable'></td>");
    td3.css("white-space", "nowrap");
    td3.click(function () {
        alert(el.FilePath);
    });
    td3.text(el.FileName);

    let tr = $("<tr></tr>");
    tr.append(td1);
    tr.append(td2);
    tr.append(td3);

    $("table#table-downloads").prepend(tr);

    currentLogs.push(el.FilePath);
    while (currentLogs.length > maxLogs) {
        currentLogs.splice(0, 1);
        $("table#table-downloads tr").slice(-1).remove();
    }
}

function startStop(self) {
    let btn = $(self);

    btn.addClass("disabled");
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

function updateConfig(self) {
    let btn = $(self);

    // update config
    btn.addClass("disabled");

    btn.removeClass("disabled");
}