# Ανέβασμα νέας έκδοσης στο GitHub

Αποθετήριο: https://github.com/mgiakalaras/SchoolNetworkManager

## Τρόπος 1 — με ένα διπλό κλικ (προτείνεται)

1. Αποσυμπίεσε το `SchoolNetworkManager-project-v2.11.zip` σε έναν φάκελο (π.χ. στην Επιφάνεια εργασίας).
2. Αν δεν έχεις Git: https://git-scm.com/download/win → εγκατάσταση με τις προεπιλογές
   (ή PowerShell: `winget install Git.Git`).
3. Διπλό κλικ στο **`ANEBASMA-STO-GITHUB.bat`**.
   - Την πρώτη φορά ανοίγει παράθυρο του GitHub → **Sign in with your browser** → Authorize.
   - Στο τέλος γράφει «✔ Έτοιμο!».

Το script κατεβάζει το αποθετήριο σε προσωρινό φάκελο, βάζει από πάνω τη νέα έκδοση,
κάνει commit και push. Δεν ανεβαίνουν ποτέ η βάση, τα αντίγραφα ασφαλείας ή τα ανεβασμένα αρχεία.

## Τρόπος 2 — GitHub Desktop

1. https://desktop.github.com → εγκατάσταση → Sign in.
2. File → Clone repository → `mgiakalaras/SchoolNetworkManager`.
3. Repository → Show in Explorer → **σβήσε τα πάντα εκτός από τον φάκελο `.git`**
   και αντέγραψε μέσα τα περιεχόμενα του zip.
4. Στο GitHub Desktop: γράψε στο Summary «School Network Manager 2.11» → **Commit to main** → **Push origin**.

## Νέο Release (η σελίδα «Releases» του αποθετηρίου)

Το README και το CHANGELOG ενημερώνονται μόνα τους με το ανέβασμα. Το Release το φτιάχνεις μία φορά ανά έκδοση:

1. GitHub → το αποθετήριο → δεξιά **Releases** → **Draft a new release**.
2. **Choose a tag** → γράψε `v2.11.0` → «Create new tag».
3. Title: `School Network Manager 2.11 (server)`.
4. Στην περιγραφή επικόλλησε το κείμενο από το `RELEASE-v2.11.md`.
5. Σύρε μέσα ως συνημμένο το `SchoolNetworkManager-portainer.tar` (για εγκατάσταση χωρίς internet).
6. Τσέκαρε **Set as the latest release** → **Publish release**.

Η παλιά έκδοση Windows: άνοιξε το Release `v1.0.0` → ✏️ Edit → άλλαξε τον τίτλο σε
`v1.0.0 — Windows (παλιά έκδοση, δεν αναπτύσσεται πλέον)` → ξε-τσέκαρε το «latest» → Update release.
Έτσι μένει διαθέσιμη, αλλά δεν μπερδεύει κανέναν.

## Μετά το ανέβασμα

Στο αποθετήριο υπάρχει έτοιμο GitHub Action (`.github/workflows/docker.yml`): σε κάθε push φτιάχνει μόνο του
το Docker image `ghcr.io/mgiakalaras/schoolnetworkmanager:latest`. Πράσινο ✔ στην καρτέλα **Actions** = χτίστηκε σωστά.

Αν θες να μην ανεβάζεις πια `.tar` στο Portainer:
1. GitHub → το προφίλ σου → **Packages** → `schoolnetworkmanager` → Package settings → **Change visibility → Public**.
2. Portainer → Stacks → schoolnetworkmanager → Editor: `image: ghcr.io/mgiakalaras/schoolnetworkmanager:latest`
3. Σε κάθε νέα έκδοση: ανέβασμα στο GitHub → περίμενε το ✔ στο Actions → Portainer → **Update the stack** με
   **Re-pull image** ανοιχτό.

⚠️ Το αποθετήριο είναι δημόσιο: μην γράψεις ποτέ τον πραγματικό `ADMIN_PASSWORD` στα αρχεία του
(`portainer-stack.yml` / `docker-compose.yml` έχουν επίτηδες «ΑΛΛΑΞΕ-ΜΕ»).
