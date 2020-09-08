# Statistics Service  :bicyclist: :bicyclist: :bicyclist:

## About this repository

[![.NET Core](https://github.com/bikedataproject/statistic-api/workflows/.NET%20Core/badge.svg)](https://github.com/bikedataproject/statistics-service/actions?query=workflow%3A%22.NET+Core%22)  

This repository contains code for the statistics service. A small service to generate basic statistics on the collected data. There are two services in this repo:

- BikeDataProject.Statistics.Service: Aggregates data from the main bike data project database onto boundaries taken from OSM.
- BikeDataProject.Statistics.Service.Tiles: Publish the aggregated data as Mapbox vector tiles.

These together generate the map on our website:

![statistics-map](./docs/screenshot1.png)

