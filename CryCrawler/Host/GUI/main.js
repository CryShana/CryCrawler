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
var usingHost = false;
var configNeedsUpdate = true;
var shouldClearCache = false;
function setStatus(data) {
    // set status
    if (data.IsListening === true && data.WorkAvailable) setStatusText("active", data.FindingValidWorks === true);
    else if (data.IsListening === true) setStatusText("idle", data.FindingValidWorks === true);
    else setStatusText("offline", data.FindingValidWorks === true);

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

    usingHost = data.UsingHost;
    if (usingHost) {
        $("#update-button").addClass("disabled");
        $("#clear-cache-button").addClass("disabled");
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

    // set logs
    var l = data.RecentDownloads.length;
    for (let i = 0; i < l; i++) {
        var el = data.RecentDownloads[i];
        if (currentLogs.indexOf(el.FilePath) === -1) addDownloadLog(el);
    }

    // display clients
    var clientList = $("#list-clients");
    data.Clients.forEach(function (val, i) {
        let id = val.Id;
        let active = val.IsActive === true ? "Active" : "Inactive";
        if (val.IsBusy === true) active = "Busy";

        let online = val.Online === true ? "Connected" : "Unreachable";
        let lconnected = val.LastConnected;
        let endpoint = val.RemoteEndpoint;
        let hosttext = val.IsHost === true ? "H" : "W";
        let workcount = val.WorkCount;
        let crawledcount = val.CrawledCount;

        let onlineClass = val.Online === true ? "green" : "red";
        let activeClass = val.IsActive === true ? "green" : "red";

        let exists = false;

        // find if it exists
        clientList.find(".client-item").each(function (ind, el) {
            let c_id_el = $(el).find(".client-id");
            let c_id = c_id_el.find("span.id").text();

            if (id === c_id) {
                exists = true;

                // update values
                let c_online = $(el).find(".client-online");
                c_online.text(online);
                c_online.removeClass("red");
                c_online.removeClass("green");
                c_online.addClass(onlineClass);

                let c_active = c_id_el.find("span.active");
                c_active.removeClass("red");
                c_active.removeClass("green");
                c_active.addClass(activeClass);
                c_active.text("(" + active + ")");

                let c_last = $(el).find(".client-last");
                c_last.html(`${lconnected} (${endpoint})`);

                let c_host = $(el).find(".client-host");
                c_host.html(`${hosttext} <span>(${crawledcount} / ${workcount})</span>`);

                // break loop
                return false;
            }
        });

        if (exists === false) {
            clientList.append($(`<div class="client-item">
                <div class="client-id"><span class="id">${id}</span> <span class="active ${activeClass}">(${active})</span></div>
                <div class="client-online ${onlineClass}">${online}</div>
                <div class="client-host">${hosttext} <span>(${crawledcount} / ${workcount})</span></div>
                <div class="client-last">${lconnected} (${endpoint})</div>
            </div>`));
        }
    });

    // check if clientList contains extra clients that need to be removed
    clientList.find(".client-item").each(function (i, el) {
        let c_id = $(el).find(".client-id span.id").text();

        let client = data.Clients.find(function (x) { return x.Id === c_id; });
        if (client === undefined || client === null) {
            $(el).remove();
        }
    });

    // if using host is true, update it constantly - you can't edit it anyway
    if (configNeedsUpdate === true || usingHost === true) {
        // set configuration
        let allfiles = data.AllFiles;
        let seedurls = data.SeedUrls.join('\n');
        let whitelist = data.Whitelist.join('\n');
        let blacklist = data.Blacklist.join('\n');
        let extensions = data.AcceptedExtensions.join(' ');
        let mediaTypes = data.AccesptedMediaTypes.join(' ');
        let scantargets = data.ScanTargetMediaTypes.join(' ');
        let minsize = data.MinSize;
        let maxsize = data.MaxSize;

        $("#config-accept-files").attr("checked", allfiles);
        $("#config-extensions").val(extensions);
        $("#config-media-types").val(mediaTypes);
        $("#config-scan-targets").val(scantargets);
        $("#config-seeds").val(seedurls);
        $("#config-whitelist").val(whitelist);
        $("#config-blacklist").val(blacklist);
        $("#config-min-size").val(minsize);
        $("#config-max-size").val(maxsize);

        // if using host, disable config options - they are received from host
        if (usingHost === true) {
            $("#config-warning").text("Using host configuration!");
            $("#config-warning").addClass("active");

            $("#config-accept-files").addClass("disabled");
            $("#config-extensions").addClass("disabled");
            $("#config-media-types").addClass("disabled");
            $("#config-scan-targets").addClass("disabled");
            $("#config-seeds").addClass("disabled");
            $("#config-whitelist").addClass("disabled");
            $("#config-blacklist").addClass("disabled");
            $("#config-min-size").addClass("disabled");
            $("#config-max-size").addClass("disabled");
        }
        else {
            $("#update-button").removeClass("disabled");
        }


        configNeedsUpdate = false;
    }
}

function setStatusText(text, findingValidWorks) {
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

        if (findingValidWorks) {
            $("#crawler-status").text("Busy");
        }
        else {
            $("#crawler-status").text("Idle");
        }
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
    isActive = !isActive;
    updateState();
}

function clearCache(self) {
    if (usingHost === true) {
        alert("Can not clear cache when using Host as Url source!");
        return;
    }

    if (confirm("This will delete all progress and start from scratch! Are you sure?") !== true) return;

    let btn = $(self);

    shouldClearCache = true;
    btn.addClass("disabled");
    updateState();
}

function updateConfig(self) {
    if (usingHost === true) {
        alert("Can not update configuration when using Host as Url source!");
        return;
    }

    let btn = $(self);

    let allfiles = $("#config-accept-files").is(":checked");
    let extensions = $("#config-extensions").val().split(' ');
    let mediaTypes = $("#config-media-types").val().split(' ');
    let scanTargets = $("#config-scan-targets").val().split(' ');
    let seedUrls = $("#config-seeds").val().split('\n');
    let whitelist = $("#config-whitelist").val().split('\n');
    let blacklist = $("#config-blacklist").val().split('\n');
    let minsize = parseFloat($("#config-min-size").val());
    let maxsize = parseFloat($("#config-max-size").val());
    if (isNaN(minsize) || isNaN(maxsize)) {
        alert("Invalid file sizes specified!");
        return;
    }

    // update config
    btn.addClass("disabled");
    post({

        AllFiles: allfiles,
        SeedUrls: seedUrls,
        Extensions: extensions,
        MediaTypes: mediaTypes,
        ScanTargets: scanTargets,
        Whitelist: whitelist,
        Blacklist: blacklist,
        MinSize: minsize,
        MaxSize: maxsize

    }, function (s) {

        configNeedsUpdate = true;

    }, "/config");
}

function updateState() {
    post({

        IsActive: isActive,
        ClearCache: shouldClearCache

    }, function (s) {
        if (usingHost === false) {
            $("#clear-cache-button").removeClass("disabled");
        }
        shouldClearCache = false;
    });
}

function post(data, callback, endpoint = "/state") {
    $.post(endpoint, JSON.stringify(data))
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