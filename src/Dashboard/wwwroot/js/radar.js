// Turns every mini-map's radar sweep with a Transmission Antenna's dish. The plugin reports the dish's heading, how
// fast it turns and the moment it was read, once a second; between readings this works out where it is now, every
// frame, and puts it in the page-wide CSS variable --radar-angle (compass degrees, 0 = north). The maps' sweeps follow
// that variable while <html> has the class radar-synced; without an antenna they keep their own slow decorative spin.
//
// Calibration: the plugin reports where the spinning part's "forward" points, and the dish faces 90 degrees
// anticlockwise of that (its left): clicking "faces north" five times in the game gave -78, which is -90 plus the
// usual early click on something turning at a steady 50 degrees a second. That is the default. The player can still
// click a button whenever the dish faces north: each click says "the true heading is 0 right now", and the average
// of the last few clicks, kept in this browser, replaces the default.
window.pccRadar = (function () {
    var KEY = 'pccRadarNorthClicks';
    var KEEP = 5;
    var DEFAULT_OFFSET = -90;
    var model = null;
    var frame = 0;
    var root = document.documentElement;
    var clicks = load();

    function load() {
        try { return JSON.parse(localStorage.getItem(KEY) || '[]'); } catch (e) { return []; }
    }

    function save() {
        try { localStorage.setItem(KEY, JSON.stringify(clicks)); } catch (e) { /* private window: this visit only */ }
    }

    // Average of angles, the way round the circle (so 350 and 10 average to 0, not 180).
    function offset() {
        if (clicks.length === 0) return DEFAULT_OFFSET;
        var x = 0, y = 0;
        clicks.forEach(function (a) { x += Math.cos(a * Math.PI / 180); y += Math.sin(a * Math.PI / 180); });
        return Math.atan2(y, x) * 180 / Math.PI;
    }

    function raw() {
        return model.heading + model.rate * (Date.now() - model.at) / 1000;
    }

    function wrap(a) { return ((a % 360) + 360) % 360; }

    function tick() {
        if (!model) { frame = 0; return; }
        root.style.setProperty('--radar-angle', wrap(raw() + offset()).toFixed(2) + 'deg');
        frame = requestAnimationFrame(tick);
    }

    function status() {
        return { clicks: clicks.length, offset: Math.round(offset()) };
    }

    return {
        set: function (heading, rate, sampledAtMs) {
            model = { heading: heading, rate: rate, at: sampledAtMs };
            root.classList.add('radar-synced');
            if (!frame) frame = requestAnimationFrame(tick);
        },
        clear: function () {
            model = null;
            root.classList.remove('radar-synced');
        },
        // The dish faces north right now.
        markNorth: function () {
            if (model) {
                clicks.push(wrap(-raw()));
                if (clicks.length > KEEP) clicks.shift();
                save();
            }
            return status();
        },
        resetCalibration: function () {
            clicks = [];
            save();
            return status();
        },
        status: status
    };
})();
