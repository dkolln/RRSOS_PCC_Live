window.pccAlerts = {
    speak: function (text, volume) {
        if (!window.speechSynthesis) return;
        var synth = window.speechSynthesis;

        var u = new SpeechSynthesisUtterance(text);
        u.volume = volume;
        u.rate = 0.92;
        u.pitch = 1.0;

        // Chrome has a long-standing bug where cancel() immediately followed by speak() replays the new
        // utterance twice. Only cancel when something is actually queued, and give the cancellation a tick
        // to actually flush before queuing the next utterance.
        if (synth.speaking || synth.pending) {
            synth.cancel();
            setTimeout(function () { synth.speak(u); }, 50);
        } else {
            synth.speak(u);
        }
    },
    test: function (volume) {
        this.speak("Volume check", volume);
    }
};
