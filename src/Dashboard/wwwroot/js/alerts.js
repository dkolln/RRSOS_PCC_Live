window.pccAlerts = {
    speak: function (text, volume) {
        if (!window.speechSynthesis) return;
        window.speechSynthesis.cancel();
        var u = new SpeechSynthesisUtterance(text);
        u.volume = volume;
        u.rate = 0.92;
        u.pitch = 1.0;
        window.speechSynthesis.speak(u);
    },
    test: function (volume) {
        this.speak("Volume check", volume);
    }
};
