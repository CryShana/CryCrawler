$(function () {
    startPollingForStatus();
});

function startPollingForStatus() {
    setInterval(function () {
        $.post("/status", function (data) {
            setStatus(data);
        }).fail(function () {
            console.log("Invalid response or connection failed!");
        });
    }, 1000);
}

// update webgui
function setStatus(data) {

}