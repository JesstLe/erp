package com.jsh.erp.datasource.entities;

import java.math.BigDecimal;
import java.util.Date;

public class CashierSessionProductItem {
    private Long id;

    private Long sessionId;

    private Long materialId;

    private String materialNameSnap;

    private String barCodeSnap;

    private String unitSnap;

    private BigDecimal unitPrice;

    private BigDecimal qty;

    private BigDecimal amount;

    private Long salesManId;

    private BigDecimal commissionPercent;

    private BigDecimal commissionAmount;

    private Date createTime;

    private Long tenantId;

    private String deleteFlag;

    public Long getId() {
        return id;
    }

    public void setId(Long id) {
        this.id = id;
    }

    public Long getSessionId() {
        return sessionId;
    }

    public void setSessionId(Long sessionId) {
        this.sessionId = sessionId;
    }

    public Long getMaterialId() {
        return materialId;
    }

    public void setMaterialId(Long materialId) {
        this.materialId = materialId;
    }

    public String getMaterialNameSnap() {
        return materialNameSnap;
    }

    public void setMaterialNameSnap(String materialNameSnap) {
        this.materialNameSnap = materialNameSnap == null ? null : materialNameSnap.trim();
    }

    public String getBarCodeSnap() {
        return barCodeSnap;
    }

    public void setBarCodeSnap(String barCodeSnap) {
        this.barCodeSnap = barCodeSnap == null ? null : barCodeSnap.trim();
    }

    public String getUnitSnap() {
        return unitSnap;
    }

    public void setUnitSnap(String unitSnap) {
        this.unitSnap = unitSnap == null ? null : unitSnap.trim();
    }

    public BigDecimal getUnitPrice() {
        return unitPrice;
    }

    public void setUnitPrice(BigDecimal unitPrice) {
        this.unitPrice = unitPrice;
    }

    public BigDecimal getQty() {
        return qty;
    }

    public void setQty(BigDecimal qty) {
        this.qty = qty;
    }

    public BigDecimal getAmount() {
        return amount;
    }

    public void setAmount(BigDecimal amount) {
        this.amount = amount;
    }

    public Long getSalesManId() {
        return salesManId;
    }

    public void setSalesManId(Long salesManId) {
        this.salesManId = salesManId;
    }

    public BigDecimal getCommissionPercent() {
        return commissionPercent;
    }

    public void setCommissionPercent(BigDecimal commissionPercent) {
        this.commissionPercent = commissionPercent;
    }

    public BigDecimal getCommissionAmount() {
        return commissionAmount;
    }

    public void setCommissionAmount(BigDecimal commissionAmount) {
        this.commissionAmount = commissionAmount;
    }

    public Date getCreateTime() {
        return createTime;
    }

    public void setCreateTime(Date createTime) {
        this.createTime = createTime;
    }

    public Long getTenantId() {
        return tenantId;
    }

    public void setTenantId(Long tenantId) {
        this.tenantId = tenantId;
    }

    public String getDeleteFlag() {
        return deleteFlag;
    }

    public void setDeleteFlag(String deleteFlag) {
        this.deleteFlag = deleteFlag == null ? null : deleteFlag.trim();
    }
}
