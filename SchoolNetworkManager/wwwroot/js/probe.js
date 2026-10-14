// Κουμπί «Ανίχνευση» στις φόρμες Νέας Καταχώρησης / Επεξεργασίας
(function () {
    const button = document.getElementById("probeButton");
    if (!button) return;

    const panel = document.getElementById("probePanel");
    const field = (id) => document.getElementById(id);

    const ipInput = field("Computer_IpAddress");
    const nameInput = field("Computer_Name");
    const macInput = field("Computer_MacAddress");
    const typeSelect = field("Computer_MachineType");
    const veyonCheck = field("Computer_HasVeyon");
    const vendorInput = field("Computer_Vendor");
    const portsInput = field("Computer_OpenPorts");
    const isCreate = button.dataset.mode === "create";

    function escapeHtml(text) {
        const div = document.createElement("div");
        div.textContent = text == null ? "" : String(text);
        return div.innerHTML;
    }

    function showPanel(kind, html) {
        panel.className = "alert alert-" + kind;
        panel.innerHTML = html;
    }

    function useButton(label, onClick) {
        const b = document.createElement("button");
        b.type = "button";
        b.className = "btn btn-sm btn-outline-primary ms-2 py-0";
        b.textContent = label;
        b.addEventListener("click", function () {
            onClick();
            b.disabled = true;
            b.textContent = "✓";
        });
        return b;
    }

    button.addEventListener("click", async function () {
        const ip = (ipInput.value || "").trim();

        if (!ip) {
            showPanel("warning", "Γράψε πρώτα την IP της συσκευής.");
            ipInput.focus();
            return;
        }

        const originalText = button.textContent;
        button.disabled = true;
        button.textContent = "⏳ Αναζήτηση...";
        showPanel("info", "Έλεγχος της " + escapeHtml(ip) + "… (λίγα δευτερόλεπτα)");

        try {
            const url = button.dataset.probeUrl + "?ip=" + encodeURIComponent(ip);
            const response = await fetch(url, { headers: { "Accept": "application/json" } });
            const contentType = response.headers.get("content-type") || "";

            if (!contentType.includes("application/json")) {
                throw new Error("Η σύνδεση έληξε. Ανανέωσε τη σελίδα και συνδέσου ξανά.");
            }

            const data = await response.json();

            if (!response.ok) {
                throw new Error(data.error || "Η ανίχνευση απέτυχε.");
            }

            const lines = [];
            const actions = [];

            lines.push(data.online
                ? "<strong>🟢 Η συσκευή απαντά.</strong>"
                : "<strong>🔴 Η συσκευή δεν απάντησε</strong> (κλειστή ή μπλοκάρει τον έλεγχο).");

            // Όνομα
            if (data.name) {
                if (!nameInput.value.trim()) {
                    nameInput.value = data.name;
                    lines.push("Όνομα: <strong>" + escapeHtml(data.name) + "</strong> (συμπληρώθηκε)");
                } else if (nameInput.value.trim().toUpperCase() !== data.name.toUpperCase()) {
                    lines.push("Όνομα στο δίκτυο: <strong>" + escapeHtml(data.name) + "</strong>");
                    actions.push(["Χρήση ονόματος", () => nameInput.value = data.name]);
                } else {
                    lines.push("Όνομα: " + escapeHtml(data.name) + " ✓");
                }
            }

            // MAC
            if (data.mac) {
                const currentMac = macInput.value.trim().toUpperCase().replace(/-/g, ":");
                if (!currentMac) {
                    macInput.value = data.mac;
                    lines.push("MAC: <strong>" + escapeHtml(data.mac) + "</strong> (συμπληρώθηκε)");
                } else if (currentMac !== data.mac.toUpperCase()) {
                    lines.push("⚠️ Στο δίκτυο βρέθηκε <strong>άλλη MAC</strong>: " + escapeHtml(data.mac));
                    actions.push(["Χρήση νέας MAC", () => macInput.value = data.mac]);
                } else {
                    lines.push("MAC: " + escapeHtml(data.mac) + " ✓");
                }
            } else if (data.online) {
                lines.push("MAC: δεν βρέθηκε (η συσκευή ίσως είναι σε άλλο υποδίκτυο).");
            }

            // Κατασκευαστής και ports
            if (vendorInput) vendorInput.value = data.vendor || vendorInput.value;
            if (portsInput) portsInput.value = data.openPorts || "";

            if (data.vendor) {
                lines.push("Κατασκευαστής: " + escapeHtml(data.vendor));
            }

            lines.push("Ανοιχτά ports: " + (data.openPorts ? escapeHtml(data.openPorts) : "κανένα από τη λίστα"));

            // Τύπος
            if (data.suggestedType && typeSelect && typeSelect.value !== data.suggestedType) {
                const current = typeSelect.value;
                if (isCreate && (current === "Υπολογιστής" || current === "Άγνωστο" || !current)) {
                    typeSelect.value = data.suggestedType;
                    lines.push("Τύπος: <strong>" + escapeHtml(data.suggestedType) + "</strong> (πρόταση)");
                } else {
                    lines.push("Προτεινόμενος τύπος: " + escapeHtml(data.suggestedType));
                    actions.push(["Χρήση τύπου", () => typeSelect.value = data.suggestedType]);
                }
            }

            // Veyon
            if (data.hasVeyon && veyonCheck && !veyonCheck.checked) {
                veyonCheck.checked = true;
                lines.push("Veyon: βρέθηκε ✓ (τσεκαρίστηκε)");
            }

            showPanel(data.online ? "success" : "warning", lines.join("<br />"));

            if (actions.length) {
                const row = document.createElement("div");
                row.className = "mt-2";
                actions.forEach(([label, fn]) => row.appendChild(useButton(label, fn)));
                panel.appendChild(row);
            }

            panel.insertAdjacentHTML("beforeend",
                '<div class="small text-muted mt-2">Έλεγξε τα στοιχεία και πάτα Αποθήκευση.</div>');
        } catch (error) {
            showPanel("danger", escapeHtml(error.message || "Η ανίχνευση απέτυχε."));
        } finally {
            button.disabled = false;
            button.textContent = originalText;
        }
    });
})();
