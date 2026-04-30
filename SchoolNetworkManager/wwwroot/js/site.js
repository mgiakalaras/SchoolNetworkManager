(function () {
    const root = document.documentElement;
    const themeToggle = document.getElementById("themeToggle");
    const clockElement = document.getElementById("currentClock");
    const weatherElement = document.getElementById("weatherWidget");

    // Theme
    const savedTheme = localStorage.getItem("snm-theme") || "light";
    root.setAttribute("data-theme", savedTheme);
    updateThemeButton(savedTheme);

    if (themeToggle) {
        themeToggle.addEventListener("click", function () {
            const currentTheme = root.getAttribute("data-theme") || "light";
            const nextTheme = currentTheme === "dark" ? "light" : "dark";

            root.setAttribute("data-theme", nextTheme);
            localStorage.setItem("snm-theme", nextTheme);
            updateThemeButton(nextTheme);
        });
    }

    function updateThemeButton(theme) {
        if (!themeToggle) return;

        if (theme === "dark") {
            themeToggle.textContent = "☀️ Light";
            themeToggle.classList.remove("btn-outline-secondary");
            themeToggle.classList.add("btn-outline-warning");
        } else {
            themeToggle.textContent = "🌙 Dark";
            themeToggle.classList.remove("btn-outline-warning");
            themeToggle.classList.add("btn-outline-secondary");
        }
    }

    // Clock
    function updateClock() {
        if (!clockElement) return;

        const now = new Date();

        clockElement.textContent = new Intl.DateTimeFormat("el-GR", {
            weekday: "short",
            day: "2-digit",
            month: "2-digit",
            hour: "2-digit",
            minute: "2-digit"
        }).format(now);
    }

    updateClock();
    setInterval(updateClock, 1000 * 30);

    // Weather - Pythagoreio / Samos περίπου
    async function loadWeather() {
        if (!weatherElement) return;

        const latitude = 37.69;
        const longitude = 26.94;
        const place = "Σάμος";

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

            weatherElement.textContent = `${place}: ${temp}°C, ${description}, ${wind} km/h`;
        } catch (error) {
            weatherElement.textContent = "Καιρός μη διαθέσιμος";
        }
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