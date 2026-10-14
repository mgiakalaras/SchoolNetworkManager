// Σελίδα «Κωδικοί»: γεννήτρια, επιλογή συσκευών, ιστορικό
(function () {
    const $ = (id) => document.getElementById(id);

    const sets = {
        upper: "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
        lower: "abcdefghijklmnopqrstuvwxyz",
        digits: "0123456789",
        symbols: "!@#$%&*?-_=+"
    };
    const ambiguous = "0O1lI|";

    function randomInt(max) {
        const buf = new Uint32Array(1);
        const limit = Math.floor(0xFFFFFFFF / max) * max;
        do { crypto.getRandomValues(buf); } while (buf[0] >= limit);
        return buf[0] % max;
    }

    function options() {
        return {
            length: parseInt($("pwLength").value, 10),
            upper: $("pwUpper").checked,
            lower: $("pwLower").checked,
            digits: $("pwDigits").checked,
            symbols: $("pwSymbols").checked,
            avoid: $("pwAmbig").checked
        };
    }

    function generate(o) {
        let groups = [];
        if (o.upper) groups.push(sets.upper);
        if (o.lower) groups.push(sets.lower);
        if (o.digits) groups.push(sets.digits);
        if (o.symbols) groups.push(sets.symbols);
        if (!groups.length) groups.push(sets.lower);
        if (o.avoid) groups = groups.map(g => [...g].filter(ch => !ambiguous.includes(ch)).join(""));

        const all = groups.join("");
        const chars = groups.map(g => g[randomInt(g.length)]);
        while (chars.length < o.length) chars.push(all[randomInt(all.length)]);

        for (let i = chars.length - 1; i > 0; i--) {
            const j = randomInt(i + 1);
            [chars[i], chars[j]] = [chars[j], chars[i]];
        }
        return { value: chars.slice(0, o.length).join(""), pool: all.length };
    }

    function strength(length, pool) {
        const bits = Math.round(length * Math.log2(Math.max(pool, 2)));
        if (bits < 50) return { bits, label: "Αδύναμος", pct: 25, cls: "weak" };
        if (bits < 70) return { bits, label: "Μέτριος", pct: 55, cls: "fair" };
        if (bits < 90) return { bits, label: "Ισχυρός", pct: 80, cls: "good" };
        return { bits, label: "Πολύ ισχυρός", pct: 100, cls: "great" };
    }

    function syncOptions(o) {
        $("optLength").value = o.length;
        $("optUpper").value = o.upper;
        $("optLower").value = o.lower;
        $("optDigits").value = o.digits;
        $("optSymbols").value = o.symbols;
        $("optAmbig").value = o.avoid;
    }

    function refresh() {
        const o = options();
        $("pwLenLabel").textContent = o.length;
        const result = generate(o);
        $("pwOut").value = result.value;
        $("pwHidden").value = result.value;
        const s = strength(o.length, result.pool);
        $("pwBar").style.width = s.pct + "%";
        $("pwBar").className = s.cls;
        $("pwStrength").textContent = s.label + " · ~" + s.bits + " bit";
        syncOptions(o);
    }

    if ($("pwOut")) {
        $("pwNew").addEventListener("click", refresh);
        $("pwLength").addEventListener("input", refresh);
        document.querySelectorAll(".pw-opt").forEach(cb => cb.addEventListener("change", refresh));
        $("pwCopy").addEventListener("click", () => copyText($("pwOut").value, $("pwCopy")));
        refresh();
    }

    // ---------- Επιλογή στόχου ----------
    const deviceBox = document.querySelector(".target-devices");
    const otherBox = document.querySelector(".target-other");
    const unique = $("UniquePerDevice");
    const willUse = $("pwWillUse");

    function updateMode() {
        const other = $("tmOther") && $("tmOther").checked;
        if (deviceBox) deviceBox.hidden = other;
        if (otherBox) otherBox.hidden = !other;
        if (willUse) {
            willUse.textContent = !other && unique && unique.checked
                ? "Θα δημιουργηθεί νέος κωδικός για κάθε συσκευή (με τις ρυθμίσεις της γεννήτριας)."
                : "Θα καταχωρηθεί ο κωδικός της γεννήτριας: " + ($("pwOut") ? $("pwOut").value : "");
        }
    }

    document.querySelectorAll("input[name=TargetMode]").forEach(r => r.addEventListener("change", updateMode));
    if (unique) unique.addEventListener("change", updateMode);
    if ($("pwNew")) $("pwNew").addEventListener("click", updateMode);
    if ($("pwLength")) $("pwLength").addEventListener("input", updateMode);
    updateMode();

    function countPicked() {
        const n = document.querySelectorAll("input[name=DeviceIds]:checked").length;
        if ($("pickedCount")) $("pickedCount").textContent = n;
    }

    document.querySelectorAll(".picker-group").forEach(function (group) {
        const all = group.querySelector(".picker-all");
        const items = group.querySelectorAll("input[name=DeviceIds]");

        all.addEventListener("click", e => e.stopPropagation());
        all.addEventListener("change", function () {
            items.forEach(cb => { if (!cb.closest(".picker-item").hidden) cb.checked = all.checked; });
            countPicked();
        });
        items.forEach(cb => cb.addEventListener("change", countPicked));
    });
    countPicked();

    const deviceSearch = $("deviceSearch");
    if (deviceSearch) {
        deviceSearch.addEventListener("input", function () {
            const term = deviceSearch.value.trim().toLowerCase();
            document.querySelectorAll(".picker-group").forEach(function (group) {
                let any = false;
                group.querySelectorAll(".picker-item").forEach(function (item) {
                    const hit = !term || item.dataset.search.includes(term);
                    item.hidden = !hit;
                    if (hit) any = true;
                });
                group.hidden = !any;
                if (term && any) group.open = true;
            });
        });
    }

    const form = $("pwForm");
    if (form) {
        form.addEventListener("submit", function (event) {
            const other = $("tmOther") && $("tmOther").checked;
            if (!other && !document.querySelector("input[name=DeviceIds]:checked")) {
                event.preventDefault();
                alert("Διάλεξε τουλάχιστον μία συσκευή.");
            }
        });
    }

    // ---------- Ιστορικό ----------
    document.querySelectorAll(".pw-toggle").forEach(function (button) {
        button.addEventListener("click", function () {
            const mask = button.parentElement.querySelector(".pw-mask");
            const shown = mask.dataset.shown === "1";
            mask.textContent = shown ? "••••••••" : mask.dataset.secret;
            mask.dataset.shown = shown ? "0" : "1";
        });
    });

    const historySearch = $("historySearch");
    if (historySearch) {
        historySearch.addEventListener("input", function () {
            const term = historySearch.value.trim().toLowerCase();
            document.querySelectorAll(".pw-batch").forEach(function (batch) {
                batch.hidden = term && !batch.dataset.search.includes(term);
            });
        });
    }

    // ---------- Αντιγραφή ----------
    async function copyText(text, button) {
        try {
            await navigator.clipboard.writeText(text);
        } catch (e) {
            const area = document.createElement("textarea");
            area.value = text;
            document.body.appendChild(area);
            area.select();
            document.execCommand("copy");
            area.remove();
        }
        if (button) {
            const old = button.textContent;
            button.textContent = "✓";
            setTimeout(() => button.textContent = old, 1200);
        }
    }

    document.querySelectorAll(".copy-btn[data-copy]").forEach(function (button) {
        button.addEventListener("click", () => copyText(button.dataset.copy, button));
    });
})();
