// Σελίδα «Χώροι»: tooltip, αναζήτηση, μετονομασία, μαζική μετακίνηση
(function () {
    const tooltip = document.querySelector(".dev-tooltip");

    // ---------- Tooltip συσκευών ----------
    document.querySelectorAll(".dev[data-tip]").forEach(function (dev) {
        dev.addEventListener("pointerenter", function () {
            if (!tooltip) return;
            tooltip.textContent = dev.dataset.tip;
            tooltip.hidden = false;
            const r = dev.getBoundingClientRect();
            const w = tooltip.offsetWidth;
            let left = r.left + r.width / 2 - w / 2;
            left = Math.max(8, Math.min(left, window.innerWidth - w - 8));
            tooltip.style.left = left + "px";
            tooltip.style.top = (r.top - tooltip.offsetHeight - 8) + "px";
        });
        dev.addEventListener("pointerleave", function () {
            if (tooltip) tooltip.hidden = true;
        });
    });

    window.addEventListener("scroll", function () { if (tooltip) tooltip.hidden = true; }, { passive: true });

    // ---------- Αναζήτηση ----------
    const search = document.getElementById("locationSearch");
    if (search) {
        search.addEventListener("input", function () {
            const term = search.value.trim().toLowerCase();
            document.querySelectorAll(".location-card").forEach(function (card) {
                const match = !term || (card.dataset.search || "").includes(term);
                card.hidden = !match;

                card.querySelectorAll(".dev").forEach(function (dev) {
                    const hit = term && (dev.dataset.tip || "").toLowerCase().includes(term);
                    dev.classList.toggle("search-hit", !!hit);
                });
            });
        });
    }

    // ---------- Ανά κάρτα ----------
    document.querySelectorAll(".location-card").forEach(function (card) {
        const renameForm = card.querySelector(".rename-form");
        const assignBar = card.querySelector(".assign-bar");
        const idsBox = card.querySelector(".assign-ids");
        const countEl = card.querySelector(".assign-count");
        const devices = Array.from(card.querySelectorAll(".dev"));

        // Μετονομασία
        const renameToggle = card.querySelector("[data-rename-toggle]");
        if (renameToggle && renameForm) {
            renameToggle.addEventListener("click", function () {
                renameForm.hidden = false;
                const input = renameForm.querySelector("input");
                input.focus();
                input.select();
            });
            renameForm.querySelector("[data-rename-cancel]").addEventListener("click", function () {
                renameForm.hidden = true;
            });
        }

        // Μαζική μετακίνηση
        function refresh() {
            const selected = devices.filter(d => d.classList.contains("selected"));
            countEl.textContent = selected.length === 1 ? "1 επιλεγμένη" : selected.length + " επιλεγμένες";
            idsBox.innerHTML = "";
            selected.forEach(function (d) {
                const input = document.createElement("input");
                input.type = "hidden";
                input.name = "ids";
                input.value = d.dataset.id;
                idsBox.appendChild(input);
            });
        }

        function setSelectMode(on) {
            card.classList.toggle("select-mode", on);
            assignBar.hidden = !on;
            if (!on) devices.forEach(d => d.classList.remove("selected"));
            refresh();
        }

        const selectToggle = card.querySelector("[data-select-mode]");
        if (selectToggle && assignBar) {
            selectToggle.addEventListener("click", function () {
                setSelectMode(true);
                assignBar.querySelector("input[name=newLocation]").focus();
            });

            assignBar.querySelector("[data-select-cancel]").addEventListener("click", function () {
                setSelectMode(false);
            });

            assignBar.querySelectorAll("[data-select]").forEach(function (button) {
                button.addEventListener("click", function () {
                    const mode = button.dataset.select;
                    devices.forEach(function (d) {
                        const on = mode === "all" ? !d.classList.contains("empty")
                            : mode === "online" ? d.classList.contains("online")
                            : mode === "named" ? !d.classList.contains("unnamed") && !d.classList.contains("empty")
                            : false;
                        d.classList.toggle("selected", on);
                    });
                    refresh();
                });
            });

            devices.forEach(function (d) {
                d.addEventListener("click", function (event) {
                    if (!card.classList.contains("select-mode")) return;
                    event.preventDefault();
                    d.classList.toggle("selected");
                    refresh();
                });
            });

            assignBar.addEventListener("submit", function (event) {
                if (!idsBox.children.length) {
                    event.preventDefault();
                    countEl.textContent = "Επίλεξε πρώτα συσκευές";
                }
            });
        }
    });
})();

// ---------- ＋ Προσθήκη συσκευών σε χώρο ----------
(function () {
    const modalEl = document.getElementById("addDevicesModal");
    const dataEl = document.getElementById("addDevicesData");
    if (!modalEl || !dataEl || !window.bootstrap) return;

    const devices = JSON.parse(dataEl.textContent || "[]");
    const modal = new bootstrap.Modal(modalEl);
    const list = document.getElementById("addList");
    const search = document.getElementById("addSearch");
    const filter = document.getElementById("addFilter");
    const count = document.getElementById("addCount");
    const submit = document.getElementById("addSubmit");
    const roomInput = document.getElementById("addRoomName");
    const roomTitle = document.getElementById("addRoomTitle");
    const createLink = document.getElementById("addCreateLink");
    const selected = new Set();
    let room = "";

    function same(a, b) { return (a || "").trim().toLowerCase() === (b || "").trim().toLowerCase(); }
    function ipNum(ip) { return (ip || "").split(".").reduce(function (n, p) { return n * 256 + (parseInt(p, 10) || 0); }, 0); }

    function render() {
        const term = (search.value || "").trim().toLowerCase();
        const mode = filter.value;
        const rows = devices.filter(function (d) {
            if (same(d.loc, room) && !d.dyn) return false;          // ήδη εδώ
            if (mode === "none" && (d.dyn || d.loc)) return false;
            if (mode === "other" && (d.dyn || !d.loc)) return false;
            if (mode === "dynamic" && !d.dyn) return false;
            if (!term) return true;
            return [d.name, d.ip, d.mac, d.vendor, d.loc].some(function (x) { return (x || "").toLowerCase().indexOf(term) >= 0; });
        }).sort(function (a, b) { return ipNum(a.ip) - ipNum(b.ip); });

        list.innerHTML = "";

        if (!rows.length) {
            list.innerHTML = '<div class="text-muted text-center py-4">Δεν βρέθηκαν συσκευές. Δοκίμασε άλλο φίλτρο ή «Νέα καταχώρηση».</div>';
        }

        rows.forEach(function (d) {
            const label = document.createElement("label");
            label.className = "add-row" + (selected.has(d.id) ? " is-selected" : "");

            const cb = document.createElement("input");
            cb.type = "checkbox";
            cb.className = "form-check-input m-0";
            cb.checked = selected.has(d.id);
            cb.addEventListener("change", function () {
                if (cb.checked) selected.add(d.id); else selected.delete(d.id);
                label.classList.toggle("is-selected", cb.checked);
                update();
            });

            const main = document.createElement("span");
            main.className = "add-main";
            main.innerHTML = '<span class="add-icon"></span><span class="add-name"></span><span class="add-ip"></span>';
            main.querySelector(".add-icon").textContent = d.dyn ? "🔄" : d.icon;
            main.querySelector(".add-name").textContent = d.name || (d.dyn ? "dynamic" : "χωρίς όνομα");
            main.querySelector(".add-ip").textContent = d.ip;

            const meta = document.createElement("span");
            meta.className = "add-meta";
            meta.textContent = [d.online ? "● online" : "", d.vendor || "", d.loc ? "📍 " + d.loc : ""].filter(Boolean).join(" · ");

            label.appendChild(cb);
            label.appendChild(main);
            label.appendChild(meta);
            list.appendChild(label);
        });
    }

    function update() {
        count.textContent = selected.size;
        submit.disabled = selected.size === 0;
        submit.textContent = selected.size ? "Προσθήκη " + selected.size + " συσκευών" : "Προσθήκη";
        modalEl.querySelectorAll('input[name="ids"]').forEach(function (i) { i.remove(); });
        selected.forEach(function (id) {
            const hidden = document.createElement("input");
            hidden.type = "hidden";
            hidden.name = "ids";
            hidden.value = id;
            roomInput.form.appendChild(hidden);
        });
    }

    function open(name) {
        room = name;
        selected.clear();
        roomInput.value = name;
        roomTitle.textContent = name;
        createLink.href = "/Computers/Create?location=" + encodeURIComponent(name);
        search.value = "";
        const unassigned = devices.some(function (d) { return !d.dyn && !d.loc; });
        filter.value = unassigned ? "none" : "all";
        render();
        update();
        modal.show();
        setTimeout(function () { search.focus(); }, 300);
    }

    document.addEventListener("click", function (event) {
        const trigger = event.target.closest("[data-add-room]");
        if (!trigger) return;
        event.preventDefault();
        event.stopPropagation();
        open(trigger.getAttribute("data-add-room"));
    }, true);

    search.addEventListener("input", render);
    filter.addEventListener("change", render);

    // Μόλις δημιουργήθηκε ο χώρος → άνοιξε κατευθείαν την προσθήκη
    try {
        const pending = JSON.parse(document.getElementById("addDevicesOpen").textContent || "null");
        if (pending) open(pending);
    } catch (e) { }
})();
