// Κάτοψη χώρου και πλάνο θέσεων
(function () {
    const dataEl = document.getElementById("planData");
    const canvas = document.getElementById("planCanvas");
    if (!dataEl || !canvas) return;

    const data = JSON.parse(dataEl.textContent);
    const token = (document.querySelector("input[name=__RequestVerificationToken]") || {}).value || "";
    const devicesById = new Map(data.devices.map(d => [d.id, d]));
    const SHAPES = {
        desk: { icon: "", label: "", w: 22, h: 8 },
        teacher: { icon: "🧑‍🏫", label: "Έδρα", w: 14, h: 9 },
        board: { icon: "📋", label: "Πίνακας", w: 30, h: 3.5 },
        door: { icon: "🚪", label: "Πόρτα", w: 7, h: 4 },
        window: { icon: "🪟", label: "Παράθυρο", w: 14, h: 3 },
        label: { icon: "🏷️", label: "Ετικέτα", w: 14, h: 5 }
    };

    let mode = data.startMode || "view";
    let aspect = data.aspect || 1.6;
    let items = (data.items || []).map(i => ({ ...i }));
    let selected = null;
    let dirty = false;
    let seatsDirty = false;
    const seats = new Map(data.devices.filter(d => d.seat).map(d => [d.id, d.seat]));

    // Αν υπάρχει εικόνα φόντου, η κάτοψη παίρνει τις αναλογίες της
    const bg = document.getElementById("planBg");
    if (bg) {
        const useImage = () => {
            if (bg.naturalWidth && bg.naturalHeight) {
                const ratio = bg.naturalWidth / bg.naturalHeight;
                if (Math.abs(ratio - aspect) > 0.02) { aspect = ratio; dirty = dirty || !data.items.length; }
                applyAspect();
            }
        };
        if (bg.complete) useImage(); else bg.addEventListener("load", useImage);
    }

    const aspectRange = document.getElementById("aspectRange");
    if (aspectRange) {
        aspectRange.value = aspect;
        aspectRange.addEventListener("input", () => { aspect = parseFloat(aspectRange.value); dirty = true; applyAspect(); });
    }

    function applyAspect() {
        canvas.style.aspectRatio = String(aspect);
        if (aspectRange) aspectRange.value = aspect;
    }

    // ---------------- Σχεδίαση ----------------

    function deviceItem(id) {
        return items.find(i => i.kind === "device" && i.computerId === id);
    }

    function render() {
        canvas.querySelectorAll(".plan-item").forEach(el => el.remove());

        for (const item of items) {
            const el = document.createElement("div");
            el.className = "plan-item kind-" + item.kind;
            el.style.left = item.x + "%";
            el.style.top = item.y + "%";

            if (item.kind === "device") {
                const d = devicesById.get(item.computerId);
                if (!d) continue;
                el.classList.add("plan-device", "state-" + d.state);
                if (d.fault) el.classList.add("has-fault");
                if (d.veyonDown) el.classList.add("veyon-down");
                const seat = seats.get(d.id);
                el.dataset.id = d.id;
                el.title = `${d.name} · ${d.ip} · ${d.type}` + (seat ? ` · 🪑 ${seat}` : "");
                el.innerHTML =
                    `<span class="pd-dot"></span>` +
                    `<span class="pd-icon">${d.icon}</span>` +
                    `<span class="pd-name"></span>` +
                    (d.fault ? `<span class="pd-flag">🔧</span>` : "") +
                    (d.veyonDown ? `<span class="pd-flag">🛑</span>` : "") +
                    `<span class="pd-seat"></span>`;
                el.querySelector(".pd-name").textContent = d.name;
                el.querySelector(".pd-seat").textContent = seat || (mode === "seats" ? "＋ όνομα" : "");
                if (!seat) el.querySelector(".pd-seat").classList.add("is-empty");
            } else {
                const shape = SHAPES[item.kind] || SHAPES.label;
                el.style.width = item.w + "%";
                el.style.height = item.h + "%";
                el.innerHTML = `<span class="ps-text"></span>` + (mode === "layout" ? `<span class="ps-resize" title="Αλλαγή μεγέθους"></span>` : "");
                el.querySelector(".ps-text").textContent = (shape.icon ? shape.icon + " " : "") + (item.label || shape.label || "");
            }

            if (item === selected) el.classList.add("is-selected");
            el._item = item;
            canvas.appendChild(el);
        }

        document.getElementById("planEmpty").hidden = items.some(i => i.kind === "device");
        renderUnplaced();
        renderSeatList();
    }

    function renderUnplaced() {
        const box = document.getElementById("unplaced");
        if (!box) return;
        box.innerHTML = "";
        const missing = data.devices.filter(d => !deviceItem(d.id));
        document.getElementById("unplacedCount").textContent = missing.length ? `(${missing.length})` : "";

        if (!missing.length) {
            box.innerHTML = `<span class="text-muted small">Όλες οι συσκευές είναι στην κάτοψη ✓</span>`;
            return;
        }

        for (const d of missing) {
            const b = document.createElement("button");
            b.type = "button";
            b.className = "unplaced-item";
            b.textContent = `${d.icon} ${d.name}`;
            b.title = "Πρόσθεσε στην κάτοψη";
            b.addEventListener("click", () => {
                const item = { computerId: d.id, kind: "device", x: 50, y: 50, w: 0, h: 0 };
                items.push(item);
                selected = item;
                dirty = true;
                render();
            });
            box.appendChild(b);
        }
    }

    function renderSeatList() {
        const list = document.getElementById("seatList");
        if (!list) return;
        list.innerHTML = "";
        for (const d of data.devices) {
            const row = document.createElement("div");
            row.className = "seat-row";
            row.innerHTML = `<span class="seat-pc"></span><span class="seat-name"></span>`;
            row.querySelector(".seat-pc").textContent = `${d.icon} ${d.name}`;
            row.querySelector(".seat-name").textContent = seats.get(d.id) || "—";
            list.appendChild(row);
        }
    }

    // ---------------- Λειτουργίες ----------------

    function setMode(next) {
        if (mode === "layout" && next !== "layout" && dirty && !confirm("Υπάρχουν μη αποθηκευμένες αλλαγές στη διάταξη. Να συνεχίσω χωρίς αποθήκευση;")) return;
        if (mode === "seats" && next !== "seats" && seatsDirty && !confirm("Υπάρχουν μη αποθηκευμένες θέσεις. Να συνεχίσω χωρίς αποθήκευση;")) return;
        mode = next;
        selected = null;
        document.querySelectorAll(".plan-modes [data-mode]").forEach(b => {
            b.classList.toggle("active", b.dataset.mode === mode);
            b.classList.toggle("btn-primary", b.dataset.mode === mode);
            b.classList.toggle("btn-outline-secondary", b.dataset.mode !== mode);
        });
        canvas.classList.toggle("is-editing", mode === "layout");
        canvas.classList.toggle("is-seating", mode === "seats");
        document.querySelector(".side-layout").hidden = mode !== "layout";
        document.querySelector(".side-seats").hidden = mode !== "seats";
        closeSeatEditor();
        render();
    }

    document.querySelectorAll(".plan-modes [data-mode]").forEach(b => b.addEventListener("click", () => setMode(b.dataset.mode)));

    // ---------------- Σύρσιμο ----------------

    let drag = null;

    canvas.addEventListener("pointerdown", function (e) {
        const el = e.target.closest(".plan-item");

        if (mode !== "layout") return;

        if (!el) {
            selected = null;
            render();
            return;
        }

        const item = el._item;
        selected = item;
        const rect = canvas.getBoundingClientRect();
        const resizing = e.target.classList.contains("ps-resize");

        drag = {
            item,
            resizing,
            startX: e.clientX,
            startY: e.clientY,
            orig: { x: item.x, y: item.y, w: item.w, h: item.h },
            rect
        };

        canvas.setPointerCapture(e.pointerId);
        el.classList.add("is-selected");
        e.preventDefault();
    });

    canvas.addEventListener("pointermove", function (e) {
        if (!drag) return;
        const dx = (e.clientX - drag.startX) / drag.rect.width * 100;
        const dy = (e.clientY - drag.startY) / drag.rect.height * 100;
        const snap = v => Math.round(v * 2) / 2; // πλέγμα 0,5%

        if (drag.resizing) {
            drag.item.w = Math.max(2, Math.min(100 - drag.item.x, snap(drag.orig.w + dx)));
            drag.item.h = Math.max(2, Math.min(100 - drag.item.y, snap(drag.orig.h + dy)));
        } else {
            const maxX = drag.item.kind === "device" ? 100 : 100 - drag.item.w;
            const maxY = drag.item.kind === "device" ? 100 : 100 - drag.item.h;
            drag.item.x = Math.max(0, Math.min(maxX, snap(drag.orig.x + dx)));
            drag.item.y = Math.max(0, Math.min(maxY, snap(drag.orig.y + dy)));
        }

        const el = [...canvas.querySelectorAll(".plan-item")].find(x => x._item === drag.item);
        if (el) {
            el.style.left = drag.item.x + "%";
            el.style.top = drag.item.y + "%";
            if (drag.item.kind !== "device") {
                el.style.width = drag.item.w + "%";
                el.style.height = drag.item.h + "%";
            }
        }
        dirty = true;
    });

    canvas.addEventListener("pointerup", function () {
        if (drag) {
            drag = null;
            render();
        }
    });

    canvas.addEventListener("dblclick", function (e) {
        if (mode !== "layout") return;
        const el = e.target.closest(".plan-item");
        if (!el || el._item.kind === "device") return;
        const text = prompt("Κείμενο:", el._item.label || (SHAPES[el._item.kind] || {}).label || "");
        if (text !== null) {
            el._item.label = text.trim();
            dirty = true;
            render();
        }
    });

    document.addEventListener("keydown", function (e) {
        if (mode !== "layout" || !selected || e.target.matches("input, textarea")) return;

        if (e.key === "Delete" || e.key === "Backspace") {
            items = items.filter(i => i !== selected);
            selected = null;
            dirty = true;
            render();
            e.preventDefault();
            return;
        }

        const step = e.shiftKey ? 2 : 0.5;
        const moves = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] };
        if (moves[e.key]) {
            selected.x = Math.max(0, Math.min(100, selected.x + moves[e.key][0]));
            selected.y = Math.max(0, Math.min(100, selected.y + moves[e.key][1]));
            dirty = true;
            render();
            e.preventDefault();
        }
    });

    // Σχήματα
    document.querySelectorAll("[data-add]").forEach(function (b) {
        b.addEventListener("click", function () {
            const kind = b.dataset.add;
            const shape = SHAPES[kind];
            let label = shape.label;
            if (kind === "label" || kind === "desk") {
                const text = prompt(kind === "desk" ? "Κείμενο πάγκου (προαιρετικά, π.χ. «Σειρά Α»):" : "Κείμενο ετικέτας:", kind === "desk" ? "" : "");
                if (text === null) return;
                label = text.trim();
            }
            const item = { kind, label, x: 40, y: 40, w: shape.w, h: shape.h };
            items.push(item);
            selected = item;
            dirty = true;
            render();
        });
    });

    // Αυτόματη τοποθέτηση σε σειρές
    const autoBtn = document.getElementById("autoArrange");
    if (autoBtn) {
        autoBtn.addEventListener("click", function () {
            const placed = items.some(i => i.kind === "device");
            if (placed && !confirm("Να τοποθετηθούν ξανά όλες οι συσκευές σε σειρές;")) return;
            items = items.filter(i => i.kind !== "device");
            const n = data.devices.length;
            const cols = Math.max(2, Math.ceil(Math.sqrt(n * aspect)));
            const rows = Math.ceil(n / cols);
            data.devices.forEach(function (d, i) {
                const c = i % cols, r = Math.floor(i / cols);
                items.push({
                    computerId: d.id, kind: "device",
                    x: Math.round((8 + (84 * (c + 0.5)) / cols) * 2) / 2,
                    y: Math.round((14 + (78 * (r + 0.5)) / rows) * 2) / 2,
                    w: 0, h: 0
                });
            });
            if (!items.some(i => i.kind === "board")) {
                items.push({ kind: "board", label: "Πίνακας", x: 35, y: 2, w: 30, h: 3.5 });
            }
            dirty = true;
            render();
        });
    }

    async function postJson(url, body) {
        const response = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json", "RequestVerificationToken": token },
            body: JSON.stringify(body)
        });
        if (!response.ok) throw new Error("HTTP " + response.status);
        return response.json();
    }

    const saveLayout = document.getElementById("saveLayout");
    if (saveLayout) {
        saveLayout.addEventListener("click", async function () {
            const status = document.getElementById("layoutStatus");
            status.textContent = "Αποθήκευση…";
            try {
                await postJson(data.layoutUrl, { aspectRatio: aspect, items });
                dirty = false;
                status.textContent = "✓ Αποθηκεύτηκε " + new Date().toLocaleTimeString("el-GR", { hour: "2-digit", minute: "2-digit" });
            } catch (err) {
                status.textContent = "⚠️ Δεν αποθηκεύτηκε (" + err.message + "). Ανανέωσε τη σελίδα και ξαναδοκίμασε.";
            }
        });
    }

    // ---------------- Θέσεις μαθητών ----------------

    let seatEditor = null;

    function closeSeatEditor() {
        if (seatEditor) { seatEditor.remove(); seatEditor = null; }
    }

    function openSeatEditor(deviceId) {
        closeSeatEditor();
        const el = [...canvas.querySelectorAll(".plan-device")].find(x => Number(x.dataset.id) === deviceId);
        if (!el) return;
        const d = devicesById.get(deviceId);

        seatEditor = document.createElement("div");
        seatEditor.className = "seat-editor";
        seatEditor.innerHTML = `<div class="small fw-semibold mb-1"></div><input class="form-control form-control-sm" placeholder="Ονοματεπώνυμο μαθητή" />`;
        seatEditor.querySelector("div").textContent = `${d.icon} ${d.name}`;
        const input = seatEditor.querySelector("input");
        input.value = seats.get(deviceId) || "";
        seatEditor.style.left = el.style.left;
        seatEditor.style.top = el.style.top;
        canvas.appendChild(seatEditor);
        input.focus();
        input.select();

        const commit = () => {
            const value = input.value.trim();
            if (value) seats.set(deviceId, value); else seats.delete(deviceId);
            seatsDirty = true;
        };

        input.addEventListener("keydown", function (e) {
            if (e.key === "Enter") {
                commit();
                // επόμενος υπολογιστής με τη σειρά της κάτοψης (πάνω→κάτω, αριστερά→δεξιά)
                const order = items.filter(i => i.kind === "device")
                    .sort((a, b) => (a.y - b.y) || (a.x - b.x))
                    .map(i => i.computerId);
                const next = order[order.indexOf(deviceId) + 1];
                render();
                if (next) openSeatEditor(next);
                e.preventDefault();
            } else if (e.key === "Escape") {
                closeSeatEditor();
            }
        });

        input.addEventListener("blur", function () {
            setTimeout(() => {
                if (seatEditor && !seatEditor.contains(document.activeElement)) {
                    commit();
                    closeSeatEditor();
                    render();
                }
            }, 120);
        });
    }

    canvas.addEventListener("click", function (e) {
        const el = e.target.closest(".plan-device");
        if (!el) return;

        if (mode === "seats" && data.classId) {
            openSeatEditor(Number(el.dataset.id));
        } else if (mode === "view") {
            window.location.href = data.detailsUrl + el.dataset.id;
        }
    });

    const saveSeats = document.getElementById("saveSeats");
    if (saveSeats) {
        saveSeats.addEventListener("click", async function () {
            closeSeatEditor();
            const status = document.getElementById("seatsStatus");
            status.textContent = "Αποθήκευση…";
            try {
                await postJson(data.seatsUrl, {
                    classId: data.classId,
                    seats: [...seats.entries()].map(([computerId, name]) => ({ computerId, name }))
                });
                seatsDirty = false;
                status.textContent = "✓ Αποθηκεύτηκε " + new Date().toLocaleTimeString("el-GR", { hour: "2-digit", minute: "2-digit" });
            } catch (err) {
                status.textContent = "⚠️ Δεν αποθηκεύτηκε (" + err.message + ").";
            }
        });
    }

    const clearSeats = document.getElementById("clearSeats");
    if (clearSeats) {
        clearSeats.addEventListener("click", function () {
            if (!confirm("Να σβηστούν όλα τα ονόματα αυτού του τμήματος;")) return;
            seats.clear();
            seatsDirty = true;
            render();
        });
    }

    window.addEventListener("beforeunload", function (e) {
        if (dirty || seatsDirty) { e.preventDefault(); e.returnValue = ""; }
    });

    applyAspect();
    setMode(mode === "seats" && !data.classId ? "view" : mode);
})();
