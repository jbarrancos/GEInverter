# GEInverter Software
![](https://img.shields.io/badge/Language-C%23-green.svg)![](https://img.shields.io/badge/.NET-4.5-green.svg)![](https://img.shields.io/badge/dependencies-pvoutput-yellowgreen.svg)

You can use this project to read your General Electric PVIN0XKS Inverter over it's ethernet port and send the output towards PVOutput.org. 

This project was created so I can have a logger run on a unix / mono system board towards pvoutput, instead of a windows computer. Also the software delivered by GE is not always as stable and uses a MS Access database which gets corrupted constantly due to a software issue.

I've only tested it on a GE PVIN04KS together with a windows 10 installation with .net framework 4.5 and on a Hardkernel Odroid XU systemboard with mono. The project is not very cleaned up, but works for what I need and very stable (6+ months without reset). 
