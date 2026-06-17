<p align="center">
  <img src="Logo_FRAMLab.png" alt="FRAMLab" width="420">
</p>

<p align="center"><strong>A dynamic, simulation-oriented extension of the Functional Resonance Analysis Method (FRAM).</strong></p>

FRAMLab is an open-source, research-driven workbench for building, visualising and *quantitatively analysing* FRAM models. It builds on the well-established **FMV – the FRAM Model Visualiser** and extends it toward dynamic and simulation-based modelling of non-trivial socio-technical systems.

[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg)](LICENSE.md)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Blazor WebAssembly](https://img.shields.io/badge/Blazor-WebAssembly-5C2D91)
![Status: Research / Early Development](https://img.shields.io/badge/status-research%20%2F%20early%20development-orange)

---

## What is FRAM?

FRAM – the **Functional Resonance Analysis Method** – is an approach for modelling how complex socio-technical systems actually work, by describing them in terms of *functions* and the *variability* that propagates between them. Functions are linked through six aspects: **I**nput, **O**utput, **P**recondition, **R**esource, **C**ontrol and **T**ime. Where one function's Output matches another function's I/P/R/C/T aspect, a *coupling* emerges.


## What is FRAMLab?

Classical FRAM is largely a *qualitative* method. FRAMLab keeps the familiar visual modelling experience of FMV and adds a layer for attaching quantitative metadata to functions and aspects and for exploring how variability propagates through a model. The goal is to move from a static drawing of a system toward something closer to a **functional system simulator**.

FRAMLab is developed independently, in an academic research context (TU München), and is released as open source under the AGPL-3.0 licence.

---

## Relationship to FMV and the FRAM ecosystem

FRAMLab is a **derivative work of FMV – the FRAM Model Visualiser** (Copyright © 2021 Zerprize Limited, author Rees Hill), used and redistributed under the terms of the GNU Affero General Public License v3.0. We are grateful to the original authors for making FMV available to the community.


FRAMLab is the **open, research-driven branch**. To avoid any confusion within the community, we use a distinct name while clearly referencing our FRAM and FMV origins.

### Scope and boundaries

FRAMLab contains only open-source and independently developed functionality. Everything we add is our own intellectual property and is published as open source. The project deliberately **does not incorporate, reproduce or reverse-engineer any non-public or paywalled features** from other FRAM tools. Where we exchange ideas with the original developers, we keep our implementation fully independent.

---

## Features

Inherited from FMV and maintained here:

- **Visual FRAM modelling** – create functions with their six aspects (Input, Output, Precondition, Resource, Control, Time) on an interactive canvas.
- **Automatic couplings** – potential couplings are drawn whenever an Output aspect matches an I/P/R/C/T aspect on another function.
- **Model editing** – drag-to-arrange, per-function colours, adjustable label/font sizes, show/hide aspect labels, multiple rendering styles.

Research extensions (the "FMI" quantitative layer):

- **Quantitative metadata** attached to functions and aspects.
- **Analysis modes** for working with that metadata, including a spreadsheet view, **Bayesian Belief Network (BBN)**, **LOPA (Layer of Protection Analysis)** and a neuron-style propagation view.
- **Expression evaluation** for computing derived values across the model.

> ⚠️ FRAMLab is in early, research-stage development. APIs, file structures and analysis modes may change.

---

## Tech stack

- **.NET 8** / **C#**
- **Blazor WebAssembly** (client-side, runs entirely in the browser)
- `Blazored.LocalStorage`, `Microsoft.AspNetCore.Components.QuickGrid`, `Microsoft.Extensions.Localization`
- Service worker for offline / PWA support

---

## Getting started

### Prerequisites

- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) or later

### Run locally

```bash
git clone https://github.com/ahmed-aoued/FRAMLab.git
cd FRAMLab
dotnet restore
dotnet run
```

Then open the URL printed in the console (typically `https://localhost:5001`).

For a hot-reload development loop:

```bash
dotnet watch
```

### Build a production release

```bash
dotnet publish -c Release
```

The published static site is written to `bin/Release/net8.0/publish/wwwroot/` and can be hosted on any static web host (e.g. GitHub Pages, Netlify, nginx).

---

## Usage

1. Click **Add a new Function** and give it a name.
2. Add aspects (Input, Output, Precondition, Resource, Control, Time) to the selected function.
3. Name an Output the same as an I/P/R/C/T aspect on another function to create a **potential coupling**.
4. Drag functions to arrange the model; use colours and label settings for clarity.
5. Save your work as an FMV data file, or export it as SVG/PDF.
6. Open the quantitative (FMI) panel to attach metadata and explore the analysis modes.

A short in-app help dialog (the **About** screen) lists all keyboard shortcuts and editing gestures.

---

## Project structure

```
FRAMLab/
├── Pages/                  # Main application page(s)
├── Shared/MainApp/
│   ├── Models/             # Function, Aspect, Coupling, StateContainer
│   ├── Services/           # Undo, metadata, expression evaluation, project I/O
│   └── Views/
│       ├── FMV/            # Functions & couplings (core visual model)
│       ├── FMI/            # Quantitative metadata: Spreadsheet, BBN, LOPA, Neuron
│       └── Pages/          # Menu bar, sidebar, layout, PDF export, About
├── Localization/           # 15 language resource files
├── wwwroot/                # Static assets, PWA manifest/service worker, JS interop
└── Program.cs              # Blazor WASM bootstrap & DI registration
```

---

## Roadmap

FRAMLab's research direction is the **dynamic, simulation-based** extension of FRAM, for example:

- richer quantitative variability models for functions and aspects;
- time-based / dynamic propagation of variability across couplings;
- scenario and sensitivity analysis;
- reproducible, exportable analysis results for scientific use.

The roadmap evolves with ongoing research; contributions and discussion are welcome.

---

## Contributing

Issues, feature ideas and pull requests are welcome. Because FRAMLab is AGPL-3.0, all contributions are accepted under the same licence. Please keep contributions original and free of any proprietary or non-public material from other FRAM tools.

---

## License

FRAMLab is licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)**. See [LICENSE.md](LICENSE.md) for the full text.

It is a derivative of **FMV – the FRAM Model Visualiser**, Copyright © 2021 **Zerprize Limited**, also distributed under AGPL-3.0. As required by the AGPL, the complete corresponding source code of FRAMLab is available in this repository, including when the application is offered as a network service.

## Acknowledgements

- **Erik Hollnagel** – originator of the Functional Resonance Analysis Method (FRAM).
- **Rees Hill / Zerprize Limited** – authors of FMV, the FRAM Model Visualiser, on which FRAMLab is based.
- The wider FRAM community at [functionalresonance.com](https://functionalresonance.com).
