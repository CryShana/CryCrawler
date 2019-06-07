$(function () {
    startPollingForStatus();
});

function startPollingForStatus() {
    fetchStatus();
    setInterval(function () {
        fetchStatus();
    }, 1000);
}

function fetchStatus() {
    $.post("/status", function (data) {
        setStatus(data);
    }).fail(function () {
        console.log("Invalid response or connection failed!");
        setStatusText("offline");
    });
}

// update webgui
function setStatus(data) {
    // set status
    if (data.IsActive === true && data.IsWorking) setStatusText("active");
    else if (data.IsActive === true) setStatusText("idle");
    else setStatusText("offline");

    // set work mode
    $("#crawler-work-source").text(data.UsingHost === true ? "Host" : "Local");

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