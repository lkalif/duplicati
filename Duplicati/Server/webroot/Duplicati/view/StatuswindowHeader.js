Ext.define('Duplicati.view.StatuswindowHeader' ,{
    extend: 'Ext.panel.Panel',
    alias : 'widget.statuswindowheader',
    
	title : 'Main App controls',
	header: false,
	border: 0,
	layout: 'column',
	cls: 'header-panel',
	
	items: [{
		xtype: 'button',
		cls: 'main-button',
		text: 'Duplicati',
		iconCls: 'unused-marker',
		minHeight: 35,
		id: 'status-window-header-main-button',
		margin: 10,
		menu: [{
			id: 'status-window-header-pause-menu',
			text: 'Pause Duplicati',
			handler: function() { Duplicati.service.togglePause(); }
		},{
			id: 'status-window-header-pause-duration-menu',
			xtype: 'form',
			layout: 'hbox',
			plain: true,
			items: [{
				xtype: 'label',
				cls: 'x-menu-item-text',
				text: 'Pause '
			},{
				id: 'status-window-header-pause-duration-picker',
				cls: 'x-menu-item-text',
				xtype: 'numberfield',
				hideLabel: true,
				minValue: 1,
				maxValue: 600,
				value: 5,
				flex: 1,
				handler: function() { Duplicati.service.pause('5m'); }
			},{
				xtype: 'label',
				cls: 'x-menu-item-text',
				text: ' min.'
			}]
		}/*{
			text: 'Pause for x minutes',
			menu: [{
				text: 'Pause for 5 minutes',
				handler: function() { Duplicati.service.pause('5m'); }
			},{
				text: 'Pause for 15 minutes',
				handler: function() { Duplicati.service.pause('15m'); }
			},{
				text: 'Pause for 30 minutes',
				handler: function() { Duplicati.service.pause('30m'); }
			},{
				text: 'Pause for 60 minutes',
				handler: function() { Duplicati.service.pause('60m'); }
			}]
		}*/,{
			text: '*** Throttle ...'
		},{
			xtype: 'menuseparator'
		},{
			text: '*** Run backups',
			menu: [{
				text: 'All backups'
			},{
				text: 'Label: Work'
			},{
				text: 'Label: Home'
			},{
				text: 'Label: Amazon S3'
			}]
		},{
			xtype: 'menuseparator'
		},{
			text: '*** Settings'
		},{
			text: 'About',
			handler: function() { Duplicati.view.AboutWindow.show(); }
		}]
	},{
		xtype: 'label',
		html: '&nbsp;',
		columnWidth: 1
	},{
		xtype: 'panel',
		layout: 'vbox',
		align: 'center',
		margin: 5,
		border: 0,
		items: [{
			xtype: 'label',
			cls: 'throttle-label',
			text: '*** Throttle upload'
		},{
			xtype: 'slider',
			width: 200,
			value: 80,
			minValue: 0,
			maxValue: 100
		}]
	},{
		xtype: 'button',
		cls: 'add-new-backup-button',
		text: 'Add new backup',
		iconCls: 'unused-marker',
		minHeight: 35,
		margin: 10,
		handler: function() {
			Ext.create('Ext.window.Window', {
			    title: 'Add new backup',
			    bodyBorder: false,
			    layout: 'fit',
			    items: {  
			        xtype: 'backupconfigpanel'
			    }
			}).show();
		}
	},{
		xtype: 'button',
		id: 'status-window-header-pause-button',
		cls: 'pause-backup-button',
		iconCls: 'backups-paused',
		minHeight: 35,
		text: 'Pause',
		margin: 10,
		handler: function() { Duplicati.service.togglePause(); }
	}],

	initComponent: function() {

        this.callParent(arguments);
    }
});