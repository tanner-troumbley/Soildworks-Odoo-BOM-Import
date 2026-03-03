from odoo import models, api
import json

class BOMImporter(models.Model):
    _name = 'bom.importer'
    _description = 'BOM Importer'

    @api.model
    def import_bom_json(self, bom_json_str):
        """Import entire BOM in one transaction with field validation and reporting"""
        bom_data = json.loads(bom_json_str)
        property_field_map = {
            "Description": "description",
            "Material": "x_material",
            "Part Number": "default_code",
            "Weight": "x_weight"
        }

        Product = self.env['product.product']
        existing_fields = set(Product._fields.keys())

        report = {
            "products_created": [],
            "products_updated": [],
            "boms_created": [],
            "missing_fields": []
        }

        def get_or_create_product(name, properties):
            product = Product.search([('name', '=', name)], limit=1)
            vals = {}
            for prop in properties:
                field_name = property_field_map.get(prop['Name'])
                if field_name:
                    if field_name in existing_fields:
                        vals[field_name] = prop['Value']
                    else:
                        report["missing_fields"].append({
                            "product": name,
                            "property": prop['Name'],
                            "field": field_name
                        })
            if product:
                if vals:
                    product.write(vals)
                    report["products_updated"].append(name)
            else:
                vals['name'] = name
                vals['type'] = 'product'
                product = Product.create(vals)
                report["products_created"].append(name)
            return product

        def process_assembly(assembly):
            assembly_product = get_or_create_product(assembly['AssemblyName'], [])
            bom_lines = []
            for part in assembly['Parts']:
                part_product = get_or_create_product(part['FileName'], part['Properties'])
                bom_lines.append((0, 0, {
                    'product_id': part_product.id,
                    'product_qty': 1.0
                }))
            if bom_lines:
                bom = self.env['mrp.bom'].create({
                    'product_tmpl_id': assembly_product.product_tmpl_id.id,
                    'product_qty': 1.0,
                    'type': 'normal',
                    'bom_line_ids': bom_lines
                })
                report["boms_created"].append(assembly['AssemblyName'])
            for sub in assembly['SubAssemblies']:
                process_assembly(sub)

        process_assembly(bom_data)
        return report
