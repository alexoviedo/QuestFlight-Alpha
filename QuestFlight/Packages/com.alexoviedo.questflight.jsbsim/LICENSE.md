# Package licensing

The QuestFlight managed package code and native C ABI wrapper are project-owned
code. JSBSim itself is licensed under GNU LGPL-2.1-or-later. Its license text is
included at `RuntimeData/JSBSim-1.3.1/COPYING.txt`.

The bundled `aircraft/c172x/c172x.xml` declares GPL in its own file header and
also carries an upstream statement that the model is not to be sold. That is
not represented here as LGPL-only. The patched model remains development-only
and is blocked from product promotion or commercial distribution until its
license/provenance terms are resolved by the project owner.

JSBSim is kept as a replaceable shared library on Windows and Android. This
package does not imply endorsement by JSBSim contributors and makes no claim of
FAA approval, certified fidelity, training credit, or suitability for flight.
