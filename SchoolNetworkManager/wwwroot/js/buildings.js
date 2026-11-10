// Σελίδα «Κτίρια»: γέμισμα των παραθύρων (κτίριο / όροφος / χώρος) και γραμμές δυναμικότητας.
(function () {
    "use strict";

    function byId(id) { return document.getElementById(id); }

    function show(id) {
        bootstrap.Modal.getOrCreateInstance(byId(id)).show();
    }

    // Η φόρμα στέλνει στο create ή στο edit handler ανάλογα με το αν υπάρχει id.
    function setMode(form, isEdit) {
        form.action = isEdit ? form.dataset.editUrl : form.dataset.createUrl;
        form.querySelectorAll("[data-edit-only]").forEach(function (el) { el.hidden = !isEdit; });
        var title = form.querySelector("[data-title]");
        if (title) { title.textContent = isEdit ? title.dataset.edit : title.dataset.create; }
    }

    function openBuilding(btn) {
        var form = byId("buildingForm");
        var id = btn.dataset.id || "";
        form.elements.id.value = id;
        form.elements.name.value = btn.dataset.name || "";
        form.elements.description.value = btn.dataset.description || "";
        setMode(form, id !== "");
        show("buildingModal");
    }

    function openFloor(btn) {
        var form = byId("floorForm");
        var id = btn.dataset.id || "";
        form.elements.id.value = id;
        form.elements.buildingId.value = btn.dataset.building || "";
        form.elements.name.value = btn.dataset.name || "";
        form.elements.level.value = btn.dataset.level || "0";
        setMode(form, id !== "");
        show("floorModal");
    }

    function addCapRow(type, count) {
        var box = byId("capRows");
        var types = JSON.parse(box.dataset.types);
        var row = document.createElement("div");
        row.className = "input-group input-group-sm mb-1 cap-row";

        var select = document.createElement("select");
        select.name = "capType";
        select.className = "form-select";
        types.forEach(function (t) {
            var o = document.createElement("option");
            o.value = t;
            o.textContent = t;
            if (t === type) { o.selected = true; }
            select.appendChild(o);
        });

        var num = document.createElement("input");
        num.type = "number";
        num.name = "capCount";
        num.min = "0";
        num.max = "9999";
        num.className = "form-control";
        num.style.maxWidth = "90px";
        num.value = count == null ? 1 : count;

        var del = document.createElement("button");
        del.type = "button";
        del.className = "btn btn-outline-danger";
        del.textContent = "✕";
        del.setAttribute("aria-label", "Αφαίρεση γραμμής");
        del.addEventListener("click", function () { row.remove(); });

        row.append(select, num, del);
        box.appendChild(row);
    }

    function openRoom(btn) {
        var form = byId("roomForm");
        var data = btn.dataset.room ? JSON.parse(btn.dataset.room) : null;
        form.elements.id.value = data ? data.id : "";
        form.elements.name.value = data ? data.name : "";
        form.elements.floorId.value = data ? (data.floorId == null ? "" : data.floorId) : (btn.dataset.floor || "");
        form.elements.roomTypeId.value = data && data.typeId != null ? data.typeId : "";
        form.elements.description.value = data ? (data.description || "") : "";
        form.elements.autoShutdown.value = data ? (data.autoShutdown || "") : "";
        byId("capRows").innerHTML = "";
        (data ? data.caps : []).forEach(function (c) { addCapRow(c.t, c.n); });
        // Το μήνυμα επιβεβαίωσης λέει τι άλλο θα διαγραφεί (υπολογίζεται στον server)
        byId("roomDelete").dataset.confirm = data && data.deleteConfirm ? data.deleteConfirm : "Διαγραφή του χώρου;";
        setMode(form, !!data);
        show("roomModal");
    }

    document.addEventListener("click", function (e) {
        var open = e.target.closest("[data-open]");
        if (open) {
            e.preventDefault();
            var kind = open.dataset.open;
            if (kind === "building") { openBuilding(open); }
            else if (kind === "floor") { openFloor(open); }
            else if (kind === "room") { openRoom(open); }
            return;
        }

        if (e.target.closest("#capAdd")) {
            addCapRow(null, 1);
        }
    });
})();
