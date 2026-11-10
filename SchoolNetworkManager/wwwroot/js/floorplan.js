// Κάτοψη ορόφου: τοποθέτηση χώρων σε ποσοστά (0–100) του καμβά
(function () {
    const app = document.getElementById("floorPlanApp");
    const canvas = document.getElementById("fpCanvas");
    if (!app || !canvas) return;

    const data = JSON.parse(app.dataset.floor);
    const token = (document.querySelector("input[name=__RequestVerificationToken]") || {}).value || "";
    const DEFAULT_W = 14, DEFAULT_H = 10, MIN_SIZE = 4;
    const STATUS_ICON = { ok: "✅", warn: "⚠️", none: "" };

    let mode = data.startMode || "view";
    let aspect = data.aspect || 1.6;
    let dirty = false;
    let selected = null;
    let drag = null;

    // Μόνο οι χώροι με πλήρεις συντεταγμένες θεωρούνται τοποθετημένοι
    const rooms = data.rooms.map(r => ({ ...r }));
    const placed = new Set(rooms.filter(r => r.x != null && r.y != null && r.w != null && r.h != null).map(r => r.id));
    for (const r of rooms) {
        if (!placed.has(r.id)) { r.x = r.y = r.w = r.h = null; }
    }

    const snap = v => Math.round(v * 2) / 2; // πλέγμα 0,5%
    const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));

    // ---------------- Αναλογία ----------------

    function applyAspect() {
        canvas.style.aspectRatio = String(aspect);
    }

    async function postAspect(ratio) {
        try {
            const body = new FormData();
            body.append("ratio", String(ratio));
            await fetch(data.aspectUrl, { method: "POST", headers: { RequestVerificationToken: token }, body });
        } catch (e) { /* η αναλογία θα ξαναδοκιμαστεί στην επόμενη φόρτωση */ }
    }

    // Με εικόνα φόντου η κάτοψη παίρνει τις αναλογίες της (και αποθηκεύονται)
    const bg = document.getElementById("fpBg");
    if (bg) {
        const useImage = () => {
            if (!bg.naturalWidth || !bg.naturalHeight) return;
            const ratio = Math.round(bg.naturalWidth / bg.naturalHeight * 1000) / 1000;
            if (Math.abs(ratio - aspect) > 0.02) {
                aspect = ratio;
                applyAspect();
                postAspect(ratio);
            }
        };
        if (bg.complete) useImage(); else bg.addEventListener("load", useImage);
    }

    // ---------------- Σχεδίαση ----------------

    function render() {
        canvas.querySelectorAll(".fp-room").forEach(el => el.remove());
        const editing = mode === "layout";

        for (const r of rooms) {
            if (r.x == null) continue;
            const el = document.createElement(editing ? "div" : "a");
            el.className = "fp-room status-" + r.status;
            if (editing) {
                el.dataset.id = r.id;
            } else {
                el.href = r.href;
                el.title = "Άνοιγμα χώρου";
            }
            el.style.left = r.x + "%";
            el.style.top = r.y + "%";
            el.style.width = r.w + "%";
            el.style.height = r.h + "%";

            const label = document.createElement("span");
            label.className = "fp-name";
            label.textContent = r.icon + " " + r.name;
            const status = document.createElement("span");
            status.className = "fp-status";
            status.textContent = STATUS_ICON[r.status] || "";
            el.append(label, status);

            if (editing) {
                const rm = document.createElement("button");
                rm.type = "button";
                rm.className = "fp-remove";
                rm.title = "Αφαίρεση από την κάτοψη";
                rm.setAttribute("aria-label", "Αφαίρεση " + r.name);
                rm.textContent = "✕";
                const handle = document.createElement("span");
                handle.className = "fp-resize";
                handle.title = "Αλλαγή μεγέθους";
                el.append(rm, handle);
            }

            if (r === selected) el.classList.add("is-selected");
            el._room = r;
            canvas.appendChild(el);
        }

        document.getElementById("fpEmpty").hidden = rooms.some(r => r.x != null);
        renderUnplaced();
    }

    function addRoom(r, cx, cy) {
        r.w = DEFAULT_W;
        r.h = DEFAULT_H;
        r.x = snap(clamp(cx - r.w / 2, 0, 100 - r.w));
        r.y = snap(clamp(cy - r.h / 2, 0, 100 - r.h));
        selected = r;
        dirty = true;
        render();
    }

    function renderUnplaced() {
        const box = document.getElementById("fpUnplaced");
        if (!box) return;
        box.innerHTML = "";
        const missing = rooms.filter(r => r.x == null);
        document.getElementById("fpUnplacedCount").textContent = missing.length ? `(${missing.length})` : "";

        if (!rooms.length) {
            box.innerHTML = `<span class="text-muted small">Ο όροφος δεν έχει χώρους ακόμα.</span>`;
            return;
        }
        if (!missing.length) {
            box.innerHTML = `<span class="text-muted small">Όλοι οι χώροι είναι στην κάτοψη ✓</span>`;
            return;
        }

        for (const r of missing) {
            const b = document.createElement("button");
            b.type = "button";
            b.className = "unplaced-item fp-unplaced-item";
            b.textContent = r.icon + " " + r.name;
            b.title = "Κλικ ή σύρσιμο στην κάτοψη";
            b.addEventListener("pointerdown", e => startListDrag(e, r, b));
            b.addEventListener("keydown", e => {
                if (e.key === "Enter" || e.key === " ") { e.preventDefault(); addRoom(r, 50, 50); }
            });
            box.appendChild(b);
        }
    }

    // Σύρσιμο από τη λίστα «Εκτός κάτοψης» πάνω στον καμβά (απλό κλικ = κέντρο)
    function startListDrag(e, r, button) {
        if (e.button !== undefined && e.button !== 0) return;
        const startX = e.clientX, startY = e.clientY;
        let ghost = null;
        e.preventDefault();

        const move = ev => {
            if (!ghost && Math.hypot(ev.clientX - startX, ev.clientY - startY) > 5) {
                ghost = document.createElement("div");
                ghost.className = "fp-ghost";
                ghost.textContent = r.icon + " " + r.name;
                document.body.appendChild(ghost);
            }
            if (ghost) {
                ghost.style.left = ev.clientX + "px";
                ghost.style.top = ev.clientY + "px";
            }
        };

        const up = ev => {
            document.removeEventListener("pointermove", move);
            document.removeEventListener("pointerup", up);
            document.removeEventListener("pointercancel", cancel);
            if (ghost) ghost.remove();

            if (!ghost) { addRoom(r, 50, 50); return; }

            const rect = canvas.getBoundingClientRect();
            if (ev.clientX >= rect.left && ev.clientX <= rect.right && ev.clientY >= rect.top && ev.clientY <= rect.bottom) {
                addRoom(r, (ev.clientX - rect.left) / rect.width * 100, (ev.clientY - rect.top) / rect.height * 100);
            }
        };

        const cancel = () => {
            document.removeEventListener("pointermove", move);
            document.removeEventListener("pointerup", up);
            document.removeEventListener("pointercancel", cancel);
            if (ghost) ghost.remove();
        };

        document.addEventListener("pointermove", move);
        document.addEventListener("pointerup", up);
        document.addEventListener("pointercancel", cancel);
    }

    // ---------------- Λειτουργίες ----------------

    function setMode(next) {
        if (mode === "layout" && next !== "layout" && dirty && !confirm("Υπάρχουν μη αποθηκευμένες αλλαγές στη διάταξη. Να συνεχίσω χωρίς αποθήκευση;")) return;
        mode = next;
        selected = null;
        document.querySelectorAll(".fp-modes [data-mode]").forEach(b => {
            b.classList.toggle("active", b.dataset.mode === mode);
            b.classList.toggle("btn-primary", b.dataset.mode === mode);
            b.classList.toggle("btn-outline-secondary", b.dataset.mode !== mode);
        });
        canvas.classList.toggle("is-editing", mode === "layout");
        document.querySelector(".fp-side-layout").hidden = mode !== "layout";
        document.querySelector(".fp-side-view").hidden = mode === "layout";
        render();
    }

    document.querySelectorAll(".fp-modes [data-mode]").forEach(b => b.addEventListener("click", () => setMode(b.dataset.mode)));

    // ---------------- Σύρσιμο / αλλαγή μεγέθους ----------------

    canvas.addEventListener("pointerdown", function (e) {
        if (mode !== "layout") return;
        const el = e.target.closest(".fp-room");

        if (!el) { selected = null; render(); return; }
        if (e.target.closest(".fp-remove")) return; // το χειρίζεται το click

        const room = el._room;
        selected = room;
        drag = {
            room,
            resizing: e.target.classList.contains("fp-resize"),
            startX: e.clientX,
            startY: e.clientY,
            orig: { x: room.x, y: room.y, w: room.w, h: room.h },
            rect: canvas.getBoundingClientRect()
        };
        canvas.setPointerCapture(e.pointerId);
        el.classList.add("is-selected");
        e.preventDefault();
    });

    canvas.addEventListener("pointermove", function (e) {
        if (!drag) return;
        const dx = (e.clientX - drag.startX) / drag.rect.width * 100;
        const dy = (e.clientY - drag.startY) / drag.rect.height * 100;
        const r = drag.room;

        if (drag.resizing) {
            r.w = clamp(snap(drag.orig.w + dx), MIN_SIZE, 100 - r.x);
            r.h = clamp(snap(drag.orig.h + dy), MIN_SIZE, 100 - r.y);
        } else {
            r.x = clamp(snap(drag.orig.x + dx), 0, 100 - r.w);
            r.y = clamp(snap(drag.orig.y + dy), 0, 100 - r.h);
        }

        const el = [...canvas.querySelectorAll(".fp-room")].find(x => x._room === r);
        if (el) {
            el.style.left = r.x + "%";
            el.style.top = r.y + "%";
            el.style.width = r.w + "%";
            el.style.height = r.h + "%";
        }
        dirty = true;
    });

    const endDrag = () => { if (drag) { drag = null; render(); } };
    canvas.addEventListener("pointerup", endDrag);
    canvas.addEventListener("pointercancel", endDrag);

    function removeRoom(r) {
        r.x = r.y = r.w = r.h = null;
        if (selected === r) selected = null;
        dirty = true;
        render();
    }

    canvas.addEventListener("click", function (e) {
        const rm = e.target.closest(".fp-remove");
        if (mode === "layout" && rm) removeRoom(rm.closest(".fp-room")._room);
    });

    document.addEventListener("keydown", function (e) {
        if (mode !== "layout" || !selected || e.target.matches("input, textarea, button, select")) return;

        if (e.key === "Delete" || e.key === "Backspace") {
            removeRoom(selected);
            e.preventDefault();
            return;
        }

        const step = e.shiftKey ? 2 : 0.5;
        const moves = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] };
        if (moves[e.key]) {
            selected.x = clamp(selected.x + moves[e.key][0], 0, 100 - selected.w);
            selected.y = clamp(selected.y + moves[e.key][1], 0, 100 - selected.h);
            dirty = true;
            render();
            e.preventDefault();
        }
    });

    // ---------------- Αποθήκευση ----------------

    const save = document.getElementById("fpSave");
    save.addEventListener("click", async function () {
        const status = document.getElementById("fpStatus");
        status.textContent = "Αποθήκευση…";
        const layout = rooms.filter(r => r.x != null).map(r => ({ roomId: r.id, x: r.x, y: r.y, w: r.w, h: r.h }));

        try {
            const body = new FormData();
            body.append("layoutJson", JSON.stringify(layout));
            const response = await fetch(data.layoutUrl, { method: "POST", headers: { RequestVerificationToken: token }, body });
            if (!response.ok) {
                let message = "HTTP " + response.status;
                try { message = (await response.json()).message || message; } catch (e) { /* όχι JSON */ }
                throw new Error(message);
            }
            dirty = false;
            status.textContent = "✓ Αποθηκεύτηκε " + new Date().toLocaleTimeString("el-GR", { hour: "2-digit", minute: "2-digit" });
        } catch (err) {
            status.textContent = "⚠️ Δεν αποθηκεύτηκε (" + err.message + "). Ανανέωσε τη σελίδα και ξαναδοκίμασε.";
        }
    });

    // Αλλαγή εικόνας: η σελίδα φορτώνει ξανά, άρα ρωτάμε αν υπάρχουν μη αποθηκευμένα
    const file = document.getElementById("fpFile");
    if (file) {
        file.addEventListener("change", function () {
            if (!file.files.length) return;
            if (dirty && !confirm("Υπάρχουν μη αποθηκευμένες αλλαγές στη διάταξη που θα χαθούν. Να συνεχίσω με το ανέβασμα;")) {
                file.value = "";
                return;
            }
            dirty = false;
            file.form.submit();
        });
    }

    window.addEventListener("beforeunload", function (e) {
        if (dirty) { e.preventDefault(); e.returnValue = ""; }
    });

    applyAspect();
    setMode(mode);
})();
