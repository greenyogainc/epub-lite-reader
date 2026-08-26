# Store listing copy — EPUB Lite Reader

Copy-paste content for Partner Center → Store listings, one block per
language. Feature names below match the in-app translations exactly
(`src/EpubLiteReader/Strings.<culture>.resx`), so the listing and the UI
never disagree on terminology.

Fields not localized per-language in Partner Center (fill once, shared
across all listings):

- **Category**: Books & Reference
- **Copyright and trademark info**: `© 2026 Green Yoga Inc`
- **Website**: `https://greenyogainc.com/`
- **Support contact info**: `https://greenyogainc.com/contact/`
- **Privacy policy URL**: `https://greenyogainc.com/privacy/`

Privacy boundary (also reflected in the descriptions below): the reader itself
never connects to the internet — book rendering is fully local and outbound
requests from book content are blocked. The only network feature is the
optional **Contact support** page inside the About window, which loads
`https://greenyogainc.com/contact/` in an embedded view *only after the user
explicitly asks for it*, restricted to `greenyogainc.com` and
`api.greenyogainc.com` (analytics and all other third-party hosts are blocked).

---

## What's new in 1.0.4 (en-US)

```
• Reliability: switching books now fully resets search results, restoring your saved reading position works in every view mode, and rapid view-mode changes always land on the mode you picked
• Search now finds and highlights matches in Scroll (continuous) mode, and in both pages of the Facing view
• Stronger offline isolation for EPUB content: book files can no longer trigger any network request, and hardened handling of malformed or hostile EPUB archives
• New About window with an accessible, keyboard-friendly design, license details, and an opt-in Contact support page (loads greenyogainc.com only when you choose)
• Large books open without freezing the window, and Scroll mode loads chapters on demand instead of all at once
• Accessibility: all toolbar controls now expose localized names to screen readers, and the toolbar adapts to narrow windows instead of clipping
• Reading position, bookmarks, and settings are now saved crash-safely
```

(Localized "What's new" text for the other 13 languages lives in each
language section below, under its own "What's new in 1.0.4" heading; keep
every claim identical.)

---

## English (en-US)

**Description**

```
EPUB Lite Reader is a free, lightweight EPUB reader for Windows. Open a book and start reading immediately — no library to manage, no account to create, no distractions.

FEATURES
• Three reading modes: Facing (two-page book layout), Single (with full-screen mode), and Scroll (continuous chapter scrolling)
• Turn the page with a click — click to go forward, click the left edge to go back, or use the keyboard (Space, Page Up/Down, arrow keys)
• Chapter sidebar built from the book's table of contents, with a resizable pane
• Reading themes: Light, Sepia, and Dark
• Adjustable font size, font family (publisher, serif, sans-serif), line spacing, and margins
• Full-text search within the book, with match highlighting
• Bookmarks, and automatic memory of your last reading position per book
• Open via file dialog, drag-and-drop, or double-click a .epub file
• Print the current chapter through the standard Windows print dialog
• Built-in security: scripts embedded in EPUB files are stripped, and book content can make no network request at all
• Optional Contact support page in the About window — it loads the Green Yoga website only when you explicitly choose; the reader itself stays fully offline

EPUB Lite Reader supports EPUB 2 and EPUB 3 files. It's freeware, released under the MIT License, by Green Yoga Inc — the same publisher behind PDF Lite Viewer.

No ads. No telemetry. No subscription. Just reading.
```

**Product features** (bullet list field)

```
Facing, single-page, and continuous scroll reading modes
Turn pages by clicking or with the keyboard
Full-screen distraction-free reading
Chapter navigation sidebar
Light, Sepia, and Dark reading themes
Adjustable fonts, spacing, and margins
Full-text search inside the book
Bookmarks and reading position memory
Drag-and-drop and file association support
Built-in print support
Completely free, no ads or accounts
```

**Search terms** (max 7, ≤30 chars each)

```
epub reader
ebook reader
epub viewer
book reader
ebook
epub
reading app
```

---

## Spanish (es)

**Description**

```
EPUB Lite Reader es un lector de EPUB gratuito y ligero para Windows. Abre un libro y empieza a leer de inmediato — sin biblioteca que gestionar, sin cuenta que crear, sin distracciones.

CARACTERÍSTICAS
• Tres modos de lectura: Doble (vista de libro a doble página), Única (con pantalla completa) y Continuo (desplazamiento continuo de capítulos)
• Pasa de página con un clic: haz clic para avanzar, en el borde izquierdo para retroceder, o usa el teclado (Espacio, Re Pág/Av Pág, flechas)
• Panel lateral de capítulos generado a partir del índice del libro, con tamaño ajustable
• Temas de lectura: Claro, Sepia y Oscuro
• Tamaño de fuente, tipo de letra (del editor, serif, sans-serif), interlineado y márgenes ajustables
• Búsqueda de texto completo dentro del libro, con resaltado de coincidencias
• Marcadores y memoria automática de la última posición de lectura de cada libro
• Ábrelo mediante el cuadro de diálogo, arrastrando y soltando, o haciendo doble clic en un archivo .epub
• Imprime el capítulo actual desde el cuadro de diálogo de impresión estándar de Windows
• Seguridad integrada: los scripts incluidos en los archivos EPUB se eliminan, y el contenido del libro no puede realizar ninguna solicitud de red
• Página opcional de contacto de soporte en la ventana Acerca de: solo carga el sitio web de Green Yoga cuando tú lo eliges explícitamente; el lector en sí permanece totalmente sin conexión

EPUB Lite Reader es compatible con archivos EPUB 2 y EPUB 3. Es gratuito, publicado bajo la licencia MIT, por Green Yoga Inc — el mismo editor de PDF Lite Viewer.

Sin anuncios. Sin telemetría. Sin suscripción. Solo lectura.
```

**Product features**

```
Modos de lectura Doble, Única y Continuo
Pasa de página con un clic o con el teclado
Lectura a pantalla completa sin distracciones
Panel lateral de navegación por capítulos
Temas de lectura Claro, Sepia y Oscuro
Fuentes, espaciado y márgenes ajustables
Búsqueda de texto completo en el libro
Marcadores y memoria de posición de lectura
Compatible con arrastrar y soltar y asociación de archivos
Impresión integrada
Totalmente gratuito, sin anuncios ni cuentas
```

**Search terms**

```
lector epub
lector ebook
visor epub
lector de libros
ebook
epub
libro electrónico
```

**What's new in 1.0.4**

```
• Fiabilidad: cambiar de libro ahora restablece completamente los resultados de búsqueda, restaurar tu posición de lectura guardada funciona en todos los modos de vista, y los cambios rápidos de modo de vista siempre terminan en el modo que elegiste
• La búsqueda ahora encuentra y resalta coincidencias en el modo Continuo (desplazamiento continuo), y en ambas páginas de la vista Doble
• Aislamiento sin conexión más sólido para el contenido EPUB: los archivos del libro ya no pueden generar ninguna solicitud de red, y se ha reforzado el manejo de archivos EPUB dañados o maliciosos
• Nueva ventana Acerca de con un diseño accesible y compatible con teclado, detalles de la licencia, y una página opcional de contacto de soporte (carga greenyogainc.com solo cuando tú lo eliges)
• Los libros grandes se abren sin bloquear la ventana, y el modo Continuo carga los capítulos bajo demanda en lugar de todos a la vez
• Accesibilidad: todos los controles de la barra de herramientas ahora exponen nombres localizados a los lectores de pantalla, y la barra de herramientas se adapta a ventanas estrechas en lugar de recortarse
• La posición de lectura, los marcadores y la configuración ahora se guardan de forma segura ante fallos
```

---

## French (fr)

**Description**

```
EPUB Lite Reader est un lecteur EPUB gratuit et léger pour Windows. Ouvrez un livre et commencez à lire immédiatement — sans bibliothèque à gérer, sans compte à créer, sans distraction.

FONCTIONNALITÉS
• Trois modes de lecture : Double (mise en page double page), Simple (avec plein écran) et Continu (défilement continu des chapitres)
• Tournez la page d'un clic : cliquez pour avancer, sur le bord gauche pour reculer, ou utilisez le clavier (Espace, Page préc./suiv., flèches)
• Panneau latéral des chapitres généré à partir de la table des matières du livre, redimensionnable
• Thèmes de lecture : Clair, Sépia et Sombre
• Taille de police, police (éditeur, serif, sans-serif), interligne et marges réglables
• Recherche en texte intégral dans le livre, avec surlignage des résultats
• Signets, et mémorisation automatique de la dernière position de lecture par livre
• Ouverture via une boîte de dialogue, par glisser-déposer, ou en double-cliquant sur un fichier .epub
• Impression du chapitre actuel via la boîte de dialogue d'impression standard de Windows
• Sécurité intégrée : les scripts intégrés aux fichiers EPUB sont supprimés, et le contenu du livre ne peut effectuer aucune requête réseau
• Page de contact du support facultative dans la fenêtre À propos — elle ne charge le site de Green Yoga que lorsque vous le choisissez explicitement ; le lecteur lui-même reste entièrement hors ligne

EPUB Lite Reader prend en charge les fichiers EPUB 2 et EPUB 3. C'est un logiciel gratuit, publié sous licence MIT, par Green Yoga Inc — l'éditeur de PDF Lite Viewer.

Sans publicité. Sans télémétrie. Sans abonnement. Juste la lecture.
```

**Product features**

```
Modes de lecture Double, Simple et Continu
Tournez les pages d'un clic ou au clavier
Lecture plein écran sans distraction
Panneau de navigation par chapitres
Thèmes de lecture Clair, Sépia et Sombre
Polices, interligne et marges réglables
Recherche en texte intégral dans le livre
Signets et mémorisation de la position de lecture
Glisser-déposer et association de fichiers
Impression intégrée
Entièrement gratuit, sans publicité ni compte
```

**Search terms**

```
lecteur epub
lecteur ebook
visionneuse epub
lecteur de livres
ebook
epub
liseuse
```

**What's new in 1.0.4**

```
• Fiabilité : changer de livre réinitialise désormais complètement les résultats de recherche, la restauration de votre position de lecture enregistrée fonctionne dans tous les modes d'affichage, et les changements rapides de mode d'affichage aboutissent toujours au mode choisi
• La recherche trouve désormais et surligne les résultats en mode Continu (défilement continu), ainsi que sur les deux pages de la vue Double
• Isolation hors ligne renforcée pour le contenu EPUB : les fichiers du livre ne peuvent plus déclencher aucune requête réseau, et la gestion des archives EPUB corrompues ou malveillantes a été renforcée
• Nouvelle fenêtre À propos avec une conception accessible et adaptée au clavier, les détails de la licence, et une page de contact du support facultative (charge greenyogainc.com uniquement lorsque vous le choisissez)
• Les livres volumineux s'ouvrent sans geler la fenêtre, et le mode Continu charge les chapitres à la demande plutôt que tous en une fois
• Accessibilité : tous les contrôles de la barre d'outils exposent désormais des noms localisés pour les lecteurs d'écran, et la barre d'outils s'adapte aux fenêtres étroites au lieu d'être tronquée
• La position de lecture, les signets et les paramètres sont désormais enregistrés de manière sécurisée en cas de plantage
```

---

## German (de)

**Description**

```
EPUB Lite Reader ist ein kostenloser, schlanker EPUB-Reader für Windows. Öffnen Sie ein Buch und beginnen Sie sofort mit dem Lesen — keine Bibliothek zu verwalten, kein Konto zu erstellen, keine Ablenkungen.

FUNKTIONEN
• Drei Lesemodi: Doppel (Doppelseiten-Ansicht), Einzeln (mit Vollbildmodus) und Scrollen (fortlaufendes Scrollen durch Kapitel)
• Umblättern per Klick: klicken zum Vorwärtsblättern, auf den linken Rand zum Zurückblättern, oder die Tastatur nutzen (Leertaste, Bild auf/ab, Pfeiltasten)
• Kapitel-Seitenleiste aus dem Inhaltsverzeichnis des Buchs, mit anpassbarer Breite
• Lesethemen: Hell, Sepia und Dunkel
• Einstellbare Schriftgröße, Schriftart (Verlagsschrift, Serif, serifenlos), Zeilenabstand und Ränder
• Volltextsuche im Buch mit Hervorhebung der Treffer
• Lesezeichen und automatisches Merken der letzten Leseposition pro Buch
• Öffnen über Dialog, per Drag & Drop oder durch Doppelklick auf eine .epub-Datei
• Drucken des aktuellen Kapitels über den Standard-Windows-Druckdialog
• Integrierte Sicherheit: In EPUB-Dateien eingebettete Skripte werden entfernt, und Buchinhalte können überhaupt keine Netzwerkanfrage auslösen
• Optionale Kontakt-Support-Seite im Info-Fenster – sie lädt die Green-Yoga-Website nur, wenn Sie es ausdrücklich wählen; der Reader selbst bleibt vollständig offline

EPUB Lite Reader unterstützt EPUB-2- und EPUB-3-Dateien. Es ist Freeware unter der MIT-Lizenz von Green Yoga Inc — dem Herausgeber von PDF Lite Viewer.

Keine Werbung. Keine Telemetrie. Kein Abonnement. Einfach lesen.
```

**Product features**

```
Lesemodi Doppel, Einzeln und Scrollen
Umblättern per Klick oder Tastatur
Ablenkungsfreies Lesen im Vollbild
Kapitel-Navigationsleiste
Lesethemen Hell, Sepia und Dunkel
Einstellbare Schrift, Zeilenabstand und Ränder
Volltextsuche im Buch
Lesezeichen und Leseposition merken
Unterstützt Drag & Drop und Dateizuordnung
Integrierter Druck
Vollständig kostenlos, ohne Werbung oder Konto
```

**Search terms**

```
epub reader
ebook reader
epub-viewer
buch lesen
ebook
epub
lese-app
```

**What's new in 1.0.4**

```
• Zuverlässigkeit: Das Wechseln von Büchern setzt jetzt die Suchergebnisse vollständig zurück, das Wiederherstellen der gespeicherten Leseposition funktioniert in jedem Ansichtsmodus, und schnelle Wechsel des Ansichtsmodus landen immer im gewählten Modus
• Die Suche findet und hebt jetzt Treffer im Scrollen-Modus (fortlaufend) sowie auf beiden Seiten der Doppel-Ansicht hervor
• Stärkere Offline-Isolation für EPUB-Inhalte: Buchdateien können keine Netzwerkanfrage mehr auslösen, und die Verarbeitung fehlerhafter oder böswilliger EPUB-Archive wurde gehärtet
• Neues Info-Fenster mit barrierefreiem, tastaturfreundlichem Design, Lizenzdetails und einer optionalen Kontakt-Support-Seite (lädt greenyogainc.com nur, wenn Sie es wählen)
• Große Bücher öffnen sich, ohne das Fenster einzufrieren, und der Scrollen-Modus lädt Kapitel bei Bedarf statt alle auf einmal
• Barrierefreiheit: Alle Symbolleisten-Steuerelemente stellen jetzt lokalisierte Namen für Bildschirmleseprogramme bereit, und die Symbolleiste passt sich schmalen Fenstern an, statt abgeschnitten zu werden
• Leseposition, Lesezeichen und Einstellungen werden jetzt absturzsicher gespeichert
```

---

## Italian (it)

**Description**

```
EPUB Lite Reader è un lettore EPUB gratuito e leggero per Windows. Apri un libro e inizia subito a leggere — nessuna libreria da gestire, nessun account da creare, nessuna distrazione.

CARATTERISTICHE
• Tre modalità di lettura: Doppia (impaginazione a doppia pagina), Singola (con schermo intero) e Scorri (scorrimento continuo dei capitoli)
• Cambia pagina con un clic: clicca per avanzare, sul bordo sinistro per tornare indietro, oppure usa la tastiera (Spazio, Pagina su/giù, frecce)
• Pannello laterale dei capitoli generato dal sommario del libro, ridimensionabile
• Temi di lettura: Chiaro, Seppia e Scuro
• Dimensione del carattere, tipo di carattere (dell'editore, serif, sans-serif), interlinea e margini regolabili
• Ricerca nel testo completo del libro, con evidenziazione dei risultati
• Segnalibri e memorizzazione automatica dell'ultima posizione di lettura per ogni libro
• Apertura tramite finestra di dialogo, trascinamento oppure doppio clic su un file .epub
• Stampa del capitolo corrente tramite la finestra di stampa standard di Windows
• Sicurezza integrata: gli script incorporati nei file EPUB vengono rimossi, e il contenuto del libro non può effettuare alcuna richiesta di rete
• Pagina facoltativa di contatto assistenza nella finestra Informazioni: carica il sito Green Yoga solo quando lo scegli esplicitamente; il lettore stesso rimane completamente offline

EPUB Lite Reader supporta i file EPUB 2 ed EPUB 3. È gratuito, distribuito con licenza MIT, da Green Yoga Inc — lo stesso editore di PDF Lite Viewer.

Nessuna pubblicità. Nessuna telemetria. Nessun abbonamento. Solo lettura.
```

**Product features**

```
Modalità di lettura Doppia, Singola e Scorri
Cambia pagina con un clic o dalla tastiera
Lettura a schermo intero senza distrazioni
Pannello di navigazione dei capitoli
Temi di lettura Chiaro, Seppia e Scuro
Carattere, interlinea e margini regolabili
Ricerca nel testo completo del libro
Segnalibri e memoria della posizione di lettura
Supporto trascinamento e associazione file
Stampa integrata
Completamente gratuito, senza pubblicità né account
```

**Search terms**

```
lettore epub
lettore ebook
visualizzatore epub
lettore di libri
ebook
epub
app di lettura
```

**What's new in 1.0.4**

```
• Affidabilità: il cambio di libro ora azzera completamente i risultati di ricerca, il ripristino della posizione di lettura salvata funziona in ogni modalità di visualizzazione, e i cambi rapidi di modalità di visualizzazione arrivano sempre alla modalità scelta
• La ricerca ora trova ed evidenzia i risultati nella modalità Scorri (continua) e in entrambe le pagine della vista Doppia
• Isolamento offline più forte per i contenuti EPUB: i file del libro non possono più generare alcuna richiesta di rete, ed è stata rafforzata la gestione di archivi EPUB danneggiati o dannosi
• Nuova finestra Informazioni con un design accessibile e navigabile da tastiera, dettagli sulla licenza e una pagina facoltativa di contatto assistenza (carica greenyogainc.com solo quando lo scegli)
• I libri di grandi dimensioni si aprono senza bloccare la finestra, e la modalità Scorri carica i capitoli su richiesta invece che tutti insieme
• Accessibilità: tutti i controlli della barra degli strumenti ora espongono nomi localizzati agli screen reader, e la barra degli strumenti si adatta alle finestre strette invece di essere tagliata
• La posizione di lettura, i segnalibri e le impostazioni ora vengono salvati in modo sicuro anche in caso di arresto anomalo
```

---

## Portuguese – Portugal (pt)

**Description**

```
O EPUB Lite Reader é um leitor de EPUB gratuito e leve para Windows. Abra um livro e comece a ler de imediato — sem biblioteca para gerir, sem conta para criar, sem distrações.

FUNCIONALIDADES
• Três modos de leitura: Dupla (página dupla), Única (com ecrã inteiro) e Contínuo (deslocamento contínuo de capítulos)
• Mude de página com um clique: clique para avançar, na margem esquerda para recuar, ou use o teclado (Espaço, Page Up/Down, setas)
• Painel lateral de capítulos criado a partir do índice do livro, com tamanho ajustável
• Temas de leitura: Claro, Sépia e Escuro
• Tamanho de letra, tipo de letra (do editor, serif, sans-serif), espaçamento entre linhas e margens ajustáveis
• Pesquisa de texto integral no livro, com destaque das correspondências
• Marcadores e memorização automática da última posição de leitura em cada livro
• Abertura através de caixa de diálogo, arrastar e largar, ou duplo clique num ficheiro .epub
• Impressão do capítulo atual através da caixa de diálogo de impressão padrão do Windows
• Segurança integrada: os scripts incluídos nos ficheiros EPUB são removidos, e o conteúdo do livro não pode fazer qualquer pedido de rede
• Página opcional de contacto de suporte na janela Acerca de — só carrega o site da Green Yoga quando o escolhe explicitamente; o leitor em si permanece totalmente offline

O EPUB Lite Reader suporta ficheiros EPUB 2 e EPUB 3. É gratuito, distribuído sob a licença MIT, pela Green Yoga Inc — a mesma editora do PDF Lite Viewer.

Sem anúncios. Sem telemetria. Sem subscrição. Só leitura.
```

**Product features**

```
Modos de leitura Dupla, Única e Contínuo
Mude de página com um clique ou pelo teclado
Leitura em ecrã inteiro sem distrações
Painel de navegação por capítulos
Temas de leitura Claro, Sépia e Escuro
Tipo de letra, espaçamento e margens ajustáveis
Pesquisa de texto integral no livro
Marcadores e memória da posição de leitura
Suporte para arrastar e largar e associação de ficheiros
Impressão integrada
Totalmente gratuito, sem anúncios nem contas
```

**Search terms**

```
leitor epub
leitor ebook
visualizador epub
leitor de livros
ebook
epub
app de leitura
```

**What's new in 1.0.4**

```
• Fiabilidade: mudar de livro agora repõe totalmente os resultados de pesquisa, restaurar a sua posição de leitura guardada funciona em todos os modos de vista, e as alterações rápidas de modo de vista terminam sempre no modo escolhido
• A pesquisa agora encontra e destaca correspondências no modo Contínuo (deslocamento contínuo) e em ambas as páginas da vista Dupla
• Isolamento offline mais robusto para conteúdo EPUB: os ficheiros do livro já não conseguem desencadear qualquer pedido de rede, e foi reforçado o tratamento de arquivos EPUB corrompidos ou hostis
• Nova janela Acerca de com um design acessível e compatível com teclado, detalhes da licença, e uma página opcional de contacto de suporte (carrega greenyogainc.com apenas quando o escolhe)
• Livros grandes abrem sem bloquear a janela, e o modo Contínuo carrega os capítulos conforme necessário em vez de todos de uma vez
• Acessibilidade: todos os controlos da barra de ferramentas expõem agora nomes localizados para leitores de ecrã, e a barra de ferramentas adapta-se a janelas estreitas em vez de ser cortada
• A posição de leitura, os marcadores e as definições são agora guardados de forma segura em caso de falha
```

---

## Portuguese – Brazil (pt-BR)

**Description**

```
O EPUB Lite Reader é um leitor de EPUB gratuito e leve para Windows. Abra um livro e comece a ler imediatamente — sem biblioteca para gerenciar, sem conta para criar, sem distrações.

RECURSOS
• Três modos de leitura: Dupla (layout de livro com página dupla), Única (com tela cheia) e Contínuo (rolagem contínua de capítulos)
• Vire a página com um clique: clique para avançar, na borda esquerda para voltar, ou use o teclado (Espaço, Page Up/Down, setas)
• Painel lateral de capítulos criado a partir do sumário do livro, com tamanho ajustável
• Temas de leitura: Claro, Sépia e Escuro
• Tamanho de fonte, tipo de fonte (da editora, serif, sans-serif), espaçamento entre linhas e margens ajustáveis
• Pesquisa de texto completo no livro, com destaque das correspondências
• Marcadores e memorização automática da última posição de leitura de cada livro
• Abertura por caixa de diálogo, arrastar e soltar, ou clique duplo em um arquivo .epub
• Impressão do capítulo atual pela caixa de diálogo de impressão padrão do Windows
• Segurança integrada: scripts incluídos nos arquivos EPUB são removidos, e o conteúdo do livro não pode fazer nenhuma solicitação de rede
• Página opcional de contato de suporte na janela Sobre — ela carrega o site da Green Yoga somente quando você escolhe explicitamente; o leitor em si permanece totalmente offline

O EPUB Lite Reader tem suporte a arquivos EPUB 2 e EPUB 3. É um software gratuito, distribuído sob a licença MIT, pela Green Yoga Inc — a mesma editora do PDF Lite Viewer.

Sem anúncios. Sem telemetria. Sem assinatura. Só leitura.
```

**Product features**

```
Modos de leitura Dupla, Única e Contínuo
Vire páginas com um clique ou pelo teclado
Leitura em tela cheia sem distrações
Painel de navegação por capítulos
Temas de leitura Claro, Sépia e Escuro
Fonte, espaçamento e margens ajustáveis
Pesquisa de texto completo no livro
Marcadores e memória da posição de leitura
Suporte a arrastar e soltar e associação de arquivos
Impressão integrada
Totalmente gratuito, sem anúncios ou contas
```

**Search terms**

```
leitor epub
leitor ebook
visualizador epub
leitor de livros
ebook
epub
app de leitura
```

**What's new in 1.0.4**

```
• Confiabilidade: trocar de livro agora redefine totalmente os resultados de pesquisa, restaurar sua posição de leitura salva funciona em todos os modos de exibição, e mudanças rápidas de modo de exibição sempre terminam no modo escolhido
• A pesquisa agora encontra e destaca correspondências no modo Contínuo (rolagem contínua) e em ambas as páginas da visualização Dupla
• Isolamento offline mais forte para conteúdo EPUB: os arquivos do livro não podem mais disparar nenhuma solicitação de rede, e o tratamento de arquivos EPUB corrompidos ou maliciosos foi reforçado
• Nova janela Sobre com design acessível e compatível com teclado, detalhes da licença, e uma página opcional de contato de suporte (carrega greenyogainc.com somente quando você escolhe)
• Livros grandes abrem sem travar a janela, e o modo Contínuo carrega os capítulos sob demanda em vez de todos de uma vez
• Acessibilidade: todos os controles da barra de ferramentas agora expõem nomes localizados para leitores de tela, e a barra de ferramentas se adapta a janelas estreitas em vez de ser cortada
• Posição de leitura, marcadores e configurações agora são salvos de forma segura contra falhas
```

---

## Japanese (ja)

**Description**

```
EPUB Lite Readerは、Windows向けの無料で軽量なEPUBリーダーです。本を開けばすぐに読み始められます — 管理するライブラリなし、作成するアカウントなし、気の散る要素なし。

主な機能
• 3つの表示モード：見開き（見開きページ表示）、単一（全画面表示対応）、スクロール（章を連続スクロール）
• クリックでページめくり — クリックで進み、左端のクリックで戻ります。キーボード（スペース、Page Up/Down、矢印キー）でも同じ操作ができます
• 本の目次から生成される章サイドバー（幅調整可能）
• 読書テーマ：ライト、セピア、ダーク
• フォントサイズ、フォント（出版社指定、セリフ体、サンセリフ体）、行間、余白を調整可能
• 本文全体を対象にした検索機能（一致箇所をハイライト表示）
• しおり機能、本ごとに最後に読んだ位置を自動記憶
• ダイアログから開く、ドラッグ＆ドロップ、.epubファイルのダブルクリックに対応
• 標準のWindows印刷ダイアログから現在の章を印刷
• 組み込みのセキュリティ：EPUBファイルに埋め込まれたスクリプトを除去し、本の内容が一切のネットワーク通信を行うことはできません
• 「バージョン情報」ウィンドウの任意のお問い合わせサポートページ — 明示的に選択した場合にのみGreen Yogaのウェブサイトを読み込みます。リーダー自体は完全にオフラインのままです

EPUB Lite ReaderはEPUB 2およびEPUB 3形式に対応しています。PDF Lite Viewerと同じ発行元、Green Yoga Incによる、MITライセンスのフリーウェアです。

広告なし。テレメトリなし。サブスクリプションなし。ただ読むだけ。
```

**Product features**

```
見開き・単一・スクロールの3つの表示モード
クリックまたはキーボードでページめくり
気の散らない全画面読書
章ナビゲーションサイドバー
ライト・セピア・ダークの読書テーマ
フォント・行間・余白の調整
本文検索機能
しおりと読書位置の記憶
ドラッグ＆ドロップとファイル関連付けに対応
印刷機能を内蔵
完全無料、広告・アカウント不要
```

**Search terms**

```
epub リーダー
電子書籍リーダー
epub ビューア
本 リーダー
電子書籍
epub
読書アプリ
```

**What's new in 1.0.4**

```
• 信頼性の向上：本を切り替えると検索結果が完全にリセットされるようになり、保存した読書位置の復元がすべての表示モードで機能し、表示モードを素早く切り替えても必ず選択したモードになります
• 検索がスクロール（連続）モードおよび見開きビューの両方のページで一致箇所を検出しハイライト表示できるようになりました
• EPUBコンテンツのオフライン分離を強化：本のファイルがネットワーク通信を発生させることはできなくなり、破損または悪意のあるEPUBアーカイブの処理も強化されました
• アクセシブルでキーボード操作しやすいデザイン、ライセンス詳細、そして任意のお問い合わせサポートページ（選択した場合のみgreenyogainc.comを読み込み）を備えた新しい「バージョン情報」ウィンドウ
• 大きな本を開いてもウィンドウがフリーズせず、スクロールモードは章を一度にすべて読み込むのではなく必要に応じて読み込みます
• アクセシビリティ：すべてのツールバー操作にスクリーンリーダー用のローカライズされた名前が付与され、ツールバーは切り取られる代わりに幅の狭いウィンドウに適応します
• 読書位置、しおり、設定はクラッシュに強い方式で保存されるようになりました
```

---

## Korean (ko)

**Description**

```
EPUB Lite Reader는 Windows용 무료 경량 EPUB 리더입니다. 책을 열면 바로 읽기 시작할 수 있습니다 — 관리할 서재도, 만들어야 할 계정도, 방해 요소도 없습니다.

주요 기능
• 세 가지 읽기 모드: 마주보기(마주보는 페이지 보기), 단일(전체 화면 지원), 스크롤(장 연속 스크롤)
• 클릭으로 페이지 넘기기 — 클릭하면 다음으로, 왼쪽 가장자리를 클릭하면 이전으로 이동하며, 키보드(스페이스, Page Up/Down, 화살표 키)도 동일하게 작동합니다
• 책의 목차를 기반으로 한 장 사이드바(크기 조절 가능)
• 읽기 테마: 라이트, 세피아, 다크
• 글꼴 크기, 글꼴(출판사 지정, 세리프, 산세리프), 줄 간격, 여백 조정 가능
• 책 전체에서 검색 가능하며 일치 항목을 강조 표시
• 책갈피 기능 및 책마다 마지막으로 읽은 위치 자동 기억
• 대화 상자, 드래그 앤 드롭, 또는 .epub 파일 더블클릭으로 열기 가능
• 표준 Windows 인쇄 대화 상자를 통해 현재 장 인쇄
• 내장 보안 기능: EPUB 파일에 포함된 스크립트를 제거하며, 책 콘텐츠는 어떠한 네트워크 요청도 할 수 없습니다
• 정보 창의 선택적 문의 지원 페이지 — 명시적으로 선택한 경우에만 Green Yoga 웹사이트를 불러오며, 리더 자체는 완전히 오프라인 상태를 유지합니다

EPUB Lite Reader는 EPUB 2 및 EPUB 3 파일을 지원합니다. PDF Lite Viewer와 동일한 게시자인 Green Yoga Inc가 MIT 라이선스로 배포하는 프리웨어입니다.

광고 없음. 원격 측정 없음. 구독 없음. 오직 독서만.
```

**Product features**

```
마주보기, 단일, 스크롤 읽기 모드
클릭 또는 키보드로 페이지 넘기기
방해 없는 전체 화면 읽기
장 탐색 사이드바
라이트, 세피아, 다크 읽기 테마
글꼴, 줄 간격, 여백 조정
책 내 전체 텍스트 검색
책갈피 및 읽기 위치 기억
드래그 앤 드롭 및 파일 연결 지원
내장 인쇄 기능
완전 무료, 광고 및 계정 불필요
```

**Search terms**

```
epub 리더
전자책 리더
epub 뷰어
책 리더
전자책
epub
독서 앱
```

**What's new in 1.0.4**

```
• 안정성: 책을 전환하면 이제 검색 결과가 완전히 초기화되고, 저장된 읽기 위치 복원이 모든 보기 모드에서 작동하며, 보기 모드를 빠르게 전환해도 항상 선택한 모드로 정확히 전환됩니다
• 이제 검색이 스크롤(연속) 모드와 마주보기 보기의 양쪽 페이지 모두에서 일치 항목을 찾아 강조 표시합니다
• EPUB 콘텐츠에 대한 오프라인 격리 강화: 책 파일이 더 이상 어떠한 네트워크 요청도 유발할 수 없으며, 손상되거나 악의적인 EPUB 아카이브에 대한 처리가 강화되었습니다
• 접근성이 뛰어나고 키보드 친화적인 디자인, 라이선스 세부정보, 그리고 선택적 문의 지원 페이지(선택한 경우에만 greenyogainc.com을 불러옴)를 갖춘 새로운 정보 창
• 큰 책도 창을 멈추지 않고 열리며, 스크롤 모드는 모든 장을 한 번에 불러오는 대신 필요할 때마다 불러옵니다
• 접근성: 이제 모든 도구 모음 컨트롤이 스크린 리더에 지역화된 이름을 제공하며, 도구 모음이 잘리는 대신 좁은 창에 맞게 조정됩니다
• 읽기 위치, 책갈피, 설정이 이제 충돌에 안전하게 저장됩니다
```

---

## Chinese Simplified (zh-Hans)

**Description**

```
EPUB Lite Reader 是一款适用于 Windows 的免费轻量级 EPUB 阅读器。打开一本书即可立即开始阅读——无需管理书库，无需创建账户，没有任何干扰。

主要功能
• 三种阅读模式：双页（双页对照版式）、单页（支持全屏）、滚动（连续滚动章节）
• 点击翻页 — 点击页面前进，点击左侧边缘后退，也可使用键盘（空格、Page Up/Down、方向键）
• 根据书籍目录生成的章节侧边栏，可调整宽度
• 阅读主题：浅色、护眼、深色
• 可调整字号、字体（出版商字体、衬线体、无衬线体）、行距和页边距
• 支持全文搜索，并高亮显示匹配项
• 书签功能，并自动记住每本书的上次阅读位置
• 支持通过对话框打开、拖放，或双击 .epub 文件打开
• 通过标准 Windows 打印对话框打印当前章节
• 内置安全防护：移除 EPUB 文件中嵌入的脚本，书籍内容完全无法发起任何网络请求
• 关于窗口中可选的联系支持页面——仅在您明确选择时才会加载 Green Yoga 网站；阅读器本身始终完全离线

EPUB Lite Reader 支持 EPUB 2 和 EPUB 3 格式。由 Green Yoga Inc（PDF Lite Viewer 的同一发行商）根据 MIT 许可证发布的免费软件。

无广告。无遥测。无订阅。只有阅读。
```

**Product features**

```
双页、单页、滚动阅读模式
点击或使用键盘翻页
无干扰全屏阅读
章节导航侧边栏
浅色、护眼、深色阅读主题
可调整字体、行距和页边距
书内全文搜索
书签与阅读位置记忆
支持拖放和文件关联
内置打印功能
完全免费，无广告无账户
```

**Search terms**

```
epub 阅读器
电子书阅读器
epub 查看器
读书软件
电子书
epub
阅读应用
```

**What's new in 1.0.4**

```
• 可靠性：切换书籍现在会完全重置搜索结果，恢复已保存的阅读位置在所有视图模式下都能正常工作，快速切换视图模式时也总能落到您选择的模式
• 搜索现在可以在滚动（连续）模式以及双页视图的两个页面中查找并高亮显示匹配项
• 增强了 EPUB 内容的离线隔离：书籍文件不再能够触发任何网络请求，并且加强了对损坏或恶意 EPUB 压缩包的处理
• 全新的关于窗口，具有无障碍、键盘友好的设计、许可证详情，以及可选的联系支持页面（仅在您选择时才加载 greenyogainc.com）
• 大型书籍打开时不会导致窗口卡死，滚动模式改为按需加载章节，而不是一次性全部加载
• 无障碍功能：所有工具栏控件现在都为屏幕阅读器提供本地化名称，工具栏也会适应窄窗口而不会被裁剪
• 阅读位置、书签和设置现在会以防崩溃的方式安全保存
```

---

## Chinese Traditional (zh-Hant)

**Description**

```
EPUB Lite Reader 是一款適用於 Windows 的免費輕量級 EPUB 閱讀器。開啟一本書即可立即開始閱讀——無需管理書庫、無需建立帳戶、沒有任何干擾。

主要功能
• 三種閱讀模式：雙頁（雙頁對照版面）、單頁（支援全螢幕）、捲動（連續捲動章節）
• 點擊翻頁 — 點擊頁面前進，點擊左側邊緣後退，也可使用鍵盤（空白鍵、Page Up/Down、方向鍵）
• 根據書籍目錄產生的章節側邊欄，可調整寬度
• 閱讀主題：淺色、護眼、深色
• 可調整字型大小、字型（出版商字型、襯線體、無襯線體）、行距與邊界
• 支援全文搜尋，並醒目提示相符項目
• 書籤功能，並自動記住每本書的上次閱讀位置
• 支援透過對話方塊開啟、拖曳，或按兩下 .epub 檔案開啟
• 透過標準 Windows 列印對話方塊列印目前章節
• 內建安全防護：移除 EPUB 檔案中嵌入的指令碼，書籍內容完全無法發出任何網路請求
• 「關於」視窗中可選的聯絡支援頁面——僅在您明確選擇時才會載入 Green Yoga 網站；閱讀器本身始終完全離線

EPUB Lite Reader 支援 EPUB 2 與 EPUB 3 格式。由 Green Yoga Inc（PDF Lite Viewer 的同一發行商）依 MIT 授權發布的免費軟體。

無廣告。無遙測。無訂閱。只有閱讀。
```

**Product features**

```
雙頁、單頁、捲動閱讀模式
點擊或使用鍵盤翻頁
無干擾全螢幕閱讀
章節導覽側邊欄
淺色、護眼、深色閱讀主題
可調整字型、行距與邊界
書內全文搜尋
書籤與閱讀位置記憶
支援拖曳與檔案關聯
內建列印功能
完全免費，無廣告無帳戶
```

**Search terms**

```
epub 閱讀器
電子書閱讀器
epub 檢視器
讀書軟體
電子書
epub
閱讀應用程式
```

**What's new in 1.0.4**

```
• 可靠性：切換書籍現在會完全重設搜尋結果，還原已儲存的閱讀位置在所有檢視模式下都能正常運作，快速切換檢視模式時也一定會停在您選擇的模式
• 搜尋現在可以在捲動（連續）模式以及雙頁檢視的兩個頁面中找到並醒目提示相符項目
• 強化了 EPUB 內容的離線隔離：書籍檔案不再能夠觸發任何網路請求，並加強了對損壞或惡意 EPUB 封存檔的處理
• 全新的「關於」視窗，具備無障礙、鍵盤友善的設計、授權詳情，以及可選的聯絡支援頁面（僅在您選擇時才載入 greenyogainc.com）
• 大型書籍開啟時不會讓視窗凍結，捲動模式改為依需求載入章節，而非一次全部載入
• 無障礙功能：所有工具列控制項現在都會為螢幕閱讀器提供在地化名稱，工具列也會適應較窄的視窗而不會被裁切
• 閱讀位置、書籤與設定現在會以防當機的方式安全儲存
```

---

## Russian (ru)

**Description**

```
EPUB Lite Reader — бесплатная и лёгкая программа для чтения EPUB на Windows. Откройте книгу и сразу начните читать — без библиотеки для управления, без учётной записи, без отвлекающих факторов.

ВОЗМОЖНОСТИ
• Три режима чтения: Разворот (книжный разворот), Одна (с полноэкранным режимом) и Прокрутка (непрерывная прокрутка глав)
• Перелистывание щелчком: щёлкните, чтобы перейти вперёд, по левому краю — назад, либо используйте клавиатуру (пробел, Page Up/Down, стрелки)
• Боковая панель глав, построенная по оглавлению книги, с изменяемой шириной
• Темы чтения: Светлая, Сепия и Тёмная
• Настраиваемый размер шрифта, шрифт (издательский, с засечками, без засечек), межстрочный интервал и поля
• Полнотекстовый поиск по книге с подсветкой совпадений
• Закладки и автоматическое запоминание последней позиции чтения для каждой книги
• Открытие через диалоговое окно, перетаскиванием или двойным щелчком по файлу .epub
• Печать текущей главы через стандартное диалоговое окно печати Windows
• Встроенная безопасность: скрипты, встроенные в файлы EPUB, удаляются, а содержимое книги вообще не может выполнять сетевые запросы
• Дополнительная страница обращения в поддержку в окне «О программе» — она загружает сайт Green Yoga только по вашему явному выбору; сама программа остаётся полностью автономной

EPUB Lite Reader поддерживает файлы EPUB 2 и EPUB 3. Это бесплатная программа по лицензии MIT от Green Yoga Inc — того же издателя, что выпустил PDF Lite Viewer.

Без рекламы. Без телеметрии. Без подписки. Только чтение.
```

**Product features**

```
Режимы чтения: разворот, одна страница, прокрутка
Перелистывание щелчком или с клавиатуры
Полноэкранное чтение без отвлечений
Боковая панель навигации по главам
Темы чтения: светлая, сепия, тёмная
Настраиваемый шрифт, интервал и поля
Полнотекстовый поиск по книге
Закладки и память позиции чтения
Поддержка перетаскивания и связи с файлами
Встроенная печать
Полностью бесплатно, без рекламы и учётных записей
```

**Search terms**

```
epub читалка
читалка книг
epub просмотр
читать книги
электронная книга
epub
читалка
```

**What's new in 1.0.4**

```
• Надёжность: переключение книг теперь полностью сбрасывает результаты поиска, восстановление сохранённой позиции чтения работает во всех режимах просмотра, а быстрая смена режима просмотра всегда приводит к выбранному режиму
• Поиск теперь находит и подсвечивает совпадения в режиме Прокрутка (непрерывная), а также на обеих страницах режима Разворот
• Усиленная автономная изоляция содержимого EPUB: файлы книги больше не могут инициировать сетевые запросы, а обработка повреждённых или вредоносных архивов EPUB стала более надёжной
• Новое окно «О программе» с доступным, удобным для клавиатуры дизайном, сведениями о лицензии и дополнительной страницей обращения в поддержку (загружает greenyogainc.com только по вашему выбору)
• Большие книги открываются без зависания окна, а режим Прокрутка загружает главы по мере необходимости, а не все сразу
• Доступность: все элементы управления панели инструментов теперь предоставляют локализованные названия для программ чтения с экрана, а панель инструментов подстраивается под узкие окна вместо обрезания
• Позиция чтения, закладки и настройки теперь сохраняются устойчиво к сбоям
```

---

## Ukrainian (uk)

**Description**

```
EPUB Lite Reader — безкоштовна та легка програма для читання EPUB у Windows. Відкрийте книгу й одразу починайте читати — без бібліотеки для керування, без облікового запису, без відволікань.

МОЖЛИВОСТІ
• Три режими читання: Розворот (книжковий розворот), Одна (з повноекранним режимом) і Прокрутка (безперервна прокрутка розділів)
• Гортання кліком: клацніть, щоб перейти вперед, по лівому краю — назад, або скористайтеся клавіатурою (пробіл, Page Up/Down, стрілки)
• Бічна панель розділів, побудована за змістом книги, з можливістю зміни ширини
• Теми читання: Світла, Сепія та Темна
• Налаштування розміру шрифту, шрифту (видавничий, із засічками, без засічок), міжрядкового інтервалу та полів
• Повнотекстовий пошук у книзі з підсвічуванням збігів
• Закладки та автоматичне запам'ятовування останньої позиції читання для кожної книги
• Відкриття через діалогове вікно, перетягуванням або подвійним клацанням файлу .epub
• Друк поточного розділу через стандартне діалогове вікно друку Windows
• Вбудований захист: скрипти, вбудовані у файли EPUB, видаляються, а вміст книги взагалі не може виконувати мережеві запити
• Додаткова сторінка звернення до підтримки у вікні «Про програму» — вона завантажує сайт Green Yoga лише за вашим явним вибором; сама програма залишається повністю автономною

EPUB Lite Reader підтримує файли EPUB 2 та EPUB 3. Це безкоштовна програма за ліцензією MIT від Green Yoga Inc — того самого видавця, що випустив PDF Lite Viewer.

Без реклами. Без телеметрії. Без підписки. Лише читання.
```

**Product features**

```
Режими читання: розворот, одна сторінка, прокрутка
Гортання кліком або з клавіатури
Повноекранне читання без відволікань
Бічна панель навігації розділами
Теми читання: світла, сепія, темна
Налаштування шрифту, інтервалу та полів
Повнотекстовий пошук у книзі
Закладки та пам'ять позиції читання
Підтримка перетягування та зв'язку з файлами
Вбудований друк
Повністю безкоштовно, без реклами та облікових записів
```

**Search terms**

```
epub читалка
читалка книг
epub перегляд
читати книги
електронна книга
epub
читалка
```

**What's new in 1.0.4**

```
• Надійність: перемикання книг тепер повністю скидає результати пошуку, відновлення збереженої позиції читання працює в усіх режимах перегляду, а швидка зміна режиму перегляду завжди призводить до обраного режиму
• Пошук тепер знаходить і підсвічує збіги в режимі Прокрутка (безперервна), а також на обох сторінках режиму Розворот
• Посилена автономна ізоляція вмісту EPUB: файли книги більше не можуть ініціювати мережеві запити, а обробку пошкоджених або шкідливих архівів EPUB посилено
• Нове вікно «Про програму» з доступним, зручним для клавіатури дизайном, відомостями про ліцензію та додатковою сторінкою звернення до підтримки (завантажує greenyogainc.com лише за вашим вибором)
• Великі книги відкриваються без зависання вікна, а режим Прокрутка завантажує розділи за потреби, а не всі одразу
• Доступність: усі елементи керування панелі інструментів тепер надають локалізовані назви для програм читання з екрана, а панель інструментів підлаштовується під вузькі вікна замість обрізання
• Позиція читання, закладки та налаштування тепер зберігаються стійко до збоїв
```

---

## Arabic (ar)

**Description**

```
EPUB Lite Reader هو برنامج مجاني وخفيف لقراءة ملفات EPUB على ويندوز. افتح كتابًا وابدأ القراءة فورًا — بلا مكتبة لإدارتها، وبلا حساب لإنشائه، وبلا أي تشتيت.

الميزات
• ثلاثة أوضاع للقراءة: صفحتان (تخطيط كتاب بصفحتين)، صفحة واحدة (مع وضع ملء الشاشة)، وتمرير (تمرير متواصل للفصول)
• قلب الصفحات بنقرة: انقر للتقدم، وانقر على الحافة اليسرى للرجوع، أو استخدم لوحة المفاتيح (المسافة، Page Up/Down، مفاتيح الأسهم)
• لوحة جانبية للفصول يتم إنشاؤها من فهرس محتويات الكتاب، وقابلة لتغيير الحجم
• مظاهر القراءة: فاتح وبني داكن وداكن
• إمكانية ضبط حجم الخط ونوعه (خط الناشر، بأطراف زخرفية، بلا أطراف زخرفية)، وتباعد الأسطر، والهوامش
• بحث في النص الكامل داخل الكتاب مع تمييز النتائج المطابقة
• إشارات مرجعية، وحفظ تلقائي لآخر موضع قراءة في كل كتاب
• الفتح عبر مربع حوار، أو السحب والإفلات، أو النقر المزدوج على ملف ‎.epub
• طباعة الفصل الحالي عبر مربع حوار الطباعة القياسي في ويندوز
• أمان مدمج: تتم إزالة النصوص البرمجية المضمّنة في ملفات EPUB، ولا يمكن لمحتوى الكتاب إجراء أي طلب شبكة على الإطلاق
• صفحة اختيارية للتواصل مع الدعم ضمن نافذة "حول" — تُحمَّل موقع Green Yoga فقط عند اختيارك ذلك صراحةً؛ ويظل البرنامج نفسه غير متصل بالإنترنت تمامًا

يدعم EPUB Lite Reader ملفات EPUB 2 وEPUB 3. وهو برنامج مجاني صادر برخصة MIT من Green Yoga Inc — الجهة الناشرة نفسها لبرنامج PDF Lite Viewer.

بلا إعلانات. بلا تتبّع بيانات. بلا اشتراك. قراءة فقط.
```

**Product features**

```
أوضاع القراءة: صفحتان، صفحة واحدة، تمرير
قلب الصفحات بالنقر أو بلوحة المفاتيح
قراءة بملء الشاشة بلا تشتيت
لوحة جانبية للتنقل بين الفصول
مظاهر القراءة: فاتح وبني داكن وداكن
ضبط الخط والتباعد والهوامش
بحث في النص الكامل داخل الكتاب
إشارات مرجعية وحفظ موضع القراءة
دعم السحب والإفلات وربط الملفات
طباعة مدمجة
مجاني بالكامل، بلا إعلانات أو حسابات
```

**Search terms**

```
قارئ epub
قارئ كتب إلكترونية
عارض epub
قارئ كتب
كتاب إلكتروني
epub
تطبيق قراءة
```

**What's new in 1.0.4**

```
• الموثوقية: تبديل الكتب الآن يعيد ضبط نتائج البحث بالكامل، واستعادة موضع القراءة المحفوظ يعمل في جميع أوضاع العرض، والتغييرات السريعة لوضع العرض تصل دائمًا إلى الوضع الذي اخترته
• أصبح البحث الآن يجد النتائج المطابقة ويميّزها في وضع التمرير (المتواصل) وفي كلتا صفحتَي عرض "صفحتان"
• عزل أقوى دون اتصال لمحتوى EPUB: لم تعد ملفات الكتاب قادرة على إجراء أي طلب شبكة، وتم تعزيز التعامل مع أرشيفات EPUB التالفة أو الضارة
• نافذة "حول" جديدة بتصميم يسهل الوصول إليه ويدعم لوحة المفاتيح، وتفاصيل الترخيص، وصفحة اختيارية للتواصل مع الدعم (تُحمَّل greenyogainc.com فقط عند اختيارك ذلك)
• تُفتح الكتب الكبيرة دون تجميد النافذة، ويُحمِّل وضع التمرير الفصول عند الحاجة بدلاً من تحميلها جميعًا دفعة واحدة
• إمكانية الوصول: توفّر جميع عناصر تحكم شريط الأدوات الآن أسماء مترجمة لقارئات الشاشة، ويتكيّف شريط الأدوات مع النوافذ الضيقة بدلاً من أن يُقتطع
• أصبح الآن حفظ موضع القراءة والإشارات المرجعية والإعدادات آمنًا حتى في حال حدوث عطل
```
