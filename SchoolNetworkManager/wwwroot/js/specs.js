// Δείχνει μόνο τα χαρακτηριστικά που ταιριάζουν στον επιλεγμένο τύπο μηχανήματος
(function () {
    const typeSelect = document.getElementById("Computer_MachineType");
    const groups = document.querySelectorAll(".spec-group");
    const hint = document.getElementById("specTypeHint");

    if (!typeSelect || !groups.length) return;

    function update() {
        const type = typeSelect.value;
        let specific = 0;

        groups.forEach(function (group) {
            const types = (group.dataset.specTypes || "").split("|");
            const visible = types.includes("*") || types.includes(type);

            group.hidden = !visible;
            if (visible && !types.includes("*")) specific++;
        });

        if (hint) {
            hint.textContent = specific
                ? "για τύπο «" + type + "»"
                : "(για αυτόν τον τύπο υπάρχουν μόνο τα βασικά στοιχεία)";
        }
    }

    typeSelect.addEventListener("change", update);
    update();
})();
