(function () {
    const root = document.documentElement;
    const clockElement = document.getElementById("currentClock");
    const weatherElement = document.getElementById("weatherWidget");

    // Theme: κάθε θέμα είναι «τρόπος-παλέτα», π.χ. dark-teal
    function applyTheme(id) {
        const parts = (id || "light-blue").split("-");
        root.setAttribute("data-theme", parts[0] === "dark" ? "dark" : "light");
        root.setAttribute("data-bs-theme", parts[0] === "dark" ? "dark" : "light");
        root.setAttribute("data-palette", parts[1] || "blue");
        root.setAttribute("data-theme-id", id);
        markActiveTheme();
    }

    function markActiveTheme() {
        let stored = null;
        try { stored = localStorage.getItem("snm-theme-id"); } catch (e) { }
        // Ενεργό: το προσωπικό θέμα, αλλιώς η «Προεπιλογή σχολείου»
        document.querySelectorAll(".theme-option").forEach(function (item) {
            const id = item.getAttribute("data-theme-id");
            item.classList.toggle("active", stored ? id === stored : id === "");
        });
    }

    document.querySelectorAll(".theme-option").forEach(function (item) {
        item.addEventListener("click", function () {
            const id = item.getAttribute("data-theme-id");

            try {
                if (id) {
                    localStorage.setItem("snm-theme-id", id);
                } else {
                    localStorage.removeItem("snm-theme-id");
                }
                localStorage.removeItem("snm-theme");
            } catch (e) { }

            applyTheme(id || root.getAttribute("data-default-theme") || "light-blue");
        });
    });

    markActiveTheme();

    // Εκτύπωση: πάντα με φωτεινό θέμα (αλλιώς ανοιχτά γράμματα σε λευκό χαρτί = κενή σελίδα)
    let themeBeforePrint = null;
    window.addEventListener("beforeprint", function () {
        if (root.getAttribute("data-theme") === "dark") {
            themeBeforePrint = root.getAttribute("data-theme-id") || "dark-blue";
            root.setAttribute("data-theme", "light");
            root.setAttribute("data-bs-theme", "light");
        }
    });
    window.addEventListener("afterprint", function () {
        if (themeBeforePrint) {
            applyTheme(themeBeforePrint);
            themeBeforePrint = null;
        }
    });

    // Γενικό tooltip για στοιχεία με data-hint (π.χ. κελιά ιστορικού)
    (function () {
        const hinted = document.querySelectorAll("[data-hint]");
        if (!hinted.length) return;

        const tip = document.createElement("div");
        tip.className = "dev-tooltip";
        tip.hidden = true;
        document.body.appendChild(tip);

        hinted.forEach(function (el) {
            el.addEventListener("pointerenter", function () {
                tip.textContent = el.dataset.hint;
                tip.hidden = false;
                const r = el.getBoundingClientRect();
                const w = tip.offsetWidth;
                let left = r.left + r.width / 2 - w / 2;
                left = Math.max(8, Math.min(left, window.innerWidth - w - 8));
                tip.style.left = left + "px";
                tip.style.top = (r.top - tip.offsetHeight - 6) + "px";
            });
            el.addEventListener("pointerleave", function () { tip.hidden = true; });
        });

        window.addEventListener("scroll", function () { tip.hidden = true; }, { passive: true });
    })();

    // Κουμπιά που κάνουν αργή εργασία: απενεργοποίηση και μήνυμα αναμονής
    document.querySelectorAll("form.busy-form").forEach(function (form) {
        form.addEventListener("submit", function (event) {
            const button = form.querySelector("button[type=submit]");
            if (!button || event.defaultPrevented) return;   // π.χ. «Άκυρο» στο confirm
            setTimeout(function () {
                button.disabled = true;
                if (button.dataset.busyText) button.textContent = button.dataset.busyText;
            }, 0);
        });
    });

    // Clock
    function updateClock() {
        if (!clockElement) return;

        const now = new Date();

        // Στη μπάρα: ημέρα και ώρα σύντομα· πλήρης ημερομηνία στο tooltip
        const day = new Intl.DateTimeFormat("el-GR", { weekday: "short", day: "numeric", month: "numeric" }).format(now);
        const time = new Intl.DateTimeFormat("el-GR", { hour: "2-digit", minute: "2-digit", hour12: false }).format(now);

        clockElement.textContent = day + " · " + time;
        clockElement.parentElement.title = new Intl.DateTimeFormat("el-GR", { dateStyle: "full", timeStyle: "short" }).format(now);
    }

    updateClock();
    setInterval(updateClock, 1000 * 30);

    // Weather - Pythagoreio / Samos περίπου
    async function loadWeather() {
        if (!weatherElement) return;

        const latitude = parseFloat(weatherElement.dataset.lat || "37.69");
        const longitude = parseFloat(weatherElement.dataset.lon || "26.94");
        const place = weatherElement.dataset.place || "";

        try {
            const url =
                `https://api.open-meteo.com/v1/forecast` +
                `?latitude=${latitude}` +
                `&longitude=${longitude}` +
                `&current=temperature_2m,weather_code,wind_speed_10m` +
                `&timezone=auto`;

            const response = await fetch(url);

            if (!response.ok) {
                throw new Error("Weather request failed");
            }

            const data = await response.json();

            if (!data.current) {
                throw new Error("No current weather");
            }

            const temp = Math.round(data.current.temperature_2m);
            const wind = Math.round(data.current.wind_speed_10m);
            const code = data.current.weather_code;
            const description = weatherCodeToText(code);

            // Στη μπάρα μόνο θερμοκρασία· τα υπόλοιπα στο tooltip
            weatherElement.textContent = `${temp}°`;
            const wrap = document.getElementById("weatherWrap");
            const icon = document.getElementById("weatherIcon");
            if (wrap) wrap.title = `${place ? place + ": " : ""}${temp}°C, ${description}, άνεμος ${wind} km/h`;
            if (icon) icon.textContent = weatherCodeToIcon(code);
        } catch (error) {
            const wrap = document.getElementById("weatherWrap");
            if (wrap) wrap.hidden = true;
            const sep = document.querySelector("#infoPill .info-sep");
            if (sep) sep.hidden = true;
        }
    }

    function weatherCodeToIcon(code) {
        if (code === 0) return "☀️";
        if (code <= 2) return "🌤️";
        if (code === 3) return "☁️";
        if (code === 45 || code === 48) return "🌫️";
        if (code >= 51 && code <= 67) return "🌧️";
        if (code >= 71 && code <= 77) return "❄️";
        if (code >= 80 && code <= 82) return "🌦️";
        if (code >= 95) return "⛈️";
        return "🌤️";
    }

    function weatherCodeToText(code) {
        const codes = {
            0: "καθαρός",
            1: "κυρίως καθαρός",
            2: "λίγα σύννεφα",
            3: "συννεφιά",
            45: "ομίχλη",
            48: "ομίχλη",
            51: "ψιλόβροχο",
            53: "ψιλόβροχο",
            55: "ψιλόβροχο",
            61: "βροχή",
            63: "βροχή",
            65: "δυνατή βροχή",
            71: "χιόνι",
            73: "χιόνι",
            75: "δυνατό χιόνι",
            80: "μπόρες",
            81: "μπόρες",
            82: "δυνατές μπόρες",
            95: "καταιγίδα",
            96: "καταιγίδα",
            99: "καταιγίδα"
        };

        return codes[code] || "καιρός";
    }

    loadWeather();
    setInterval(loadWeather, 1000 * 60 * 30);
    // Cursor-reactive RGB glow
    function attachCursorGlow() {
        const glowElements = document.querySelectorAll(".card, .top-pill");

        glowElements.forEach(function (element) {
            element.addEventListener("pointermove", function (event) {
                const rect = element.getBoundingClientRect();

                const x = event.clientX - rect.left;
                const y = event.clientY - rect.top;

                element.style.setProperty("--mx", `${x}px`);
                element.style.setProperty("--my", `${y}px`);
            });

            element.addEventListener("pointerleave", function () {
                element.style.setProperty("--mx", "50%");
                element.style.setProperty("--my", "50%");
            });
        });
    }

    attachCursorGlow();
})();